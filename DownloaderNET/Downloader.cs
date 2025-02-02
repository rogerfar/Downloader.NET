using System.Buffers;
using System.Collections.Concurrent;
using System.Net.Http.Headers;

namespace DownloaderNET;

/**
 * The Downloader can download a file with multiple tasks, each writing to the disk directly.
 * Use the Settings object in the constructor to set additional parameters.
 * The Download function will return immediatly, monitor the OnComplete event.
 */
public class Downloader : IDisposable
{
    /**
     * The OnProgress event will periodically be fired with the current state.
     */
    public Action<IList<Chunk>, Int32>? OnProgress { get; set; }

    /**
     * The OnComplete will fire when all chunks are finished and the file has been written to disk completely.
     * The Exception parameter will be passed if an exception occurred during downloading.
     */
    public Func<Int64, Exception?, Task>? OnComplete { get; set; }

    /**
     * Log debug messages.
     */
    public Action<LogMessage, Int32>? OnLog { get; set; }

    private readonly Uri _uri;
    private readonly String _path;
    private readonly Settings _settings;
    private readonly ConcurrentQueue<FileChunk> _fileBuffer;
    private readonly HttpClient _httpClient;
    private readonly ArrayPool<Byte> _bufferPool = ArrayPool<Byte>.Shared;
    
    private DateTimeOffset _lastGc = DateTimeOffset.UtcNow;

    /// <summary>
    /// Construct the Downloader.
    /// </summary>
    /// <param name="url">The URL to download the file from.</param>
    /// <param name="path">The complete path (directory + filename) to write the file to.</param>
    /// <param name="settings">Pass additional _settings to the downloader.</param>
    public Downloader(String url, String path, Settings? settings = null)
    {
        Log("Constructor start", -1);

        Log($"Setting URL {url}", -1, 1);

        _uri = new Uri(url);
        _path = path;

        settings ??= new Settings
        {
            LogLevel = 5,
            BufferSize = 4096,
            Parallel = 8,
            ChunkSize = 0,
            MaximumBytesPerSecond = 0,
            Timeout = 30000,
            RetryCount = 0,
            UpdateTime = 1000
        };

        if (settings.BufferSize <= 0)
        {
            settings.BufferSize = 4096;
        }

        if (settings.Parallel <= 0)
        {
            settings.Parallel = 1;
        }

        if (settings.ChunkSize <= 0)
        {
            settings.ChunkSize = 0;
        }

        if (settings.MaximumBytesPerSecond < 0)
        {
            settings.MaximumBytesPerSecond = 0;
        }
        
        if (settings.Timeout <= 0)
        {
            settings.Timeout = 30000;
        }

        if (settings.RetryCount < 0)
        {
            settings.RetryCount = 0;
        }

        if (settings.UpdateTime <= 0)
        {
            settings.UpdateTime = 1000;
        }

        Log($"Setting BufferSize {settings.BufferSize}", -1);
        Log($"Setting ChunkCount {settings.Parallel}", -1);
        Log($"Setting ChunkSize {settings.ChunkSize}", -1);
        Log($"Setting MaximumBytesPerSecond {settings.MaximumBytesPerSecond}", -1);
        Log($"Setting Timeout {settings.Timeout}", -1);
        Log($"Setting RetryCount {settings.RetryCount}", -1);
        Log($"Setting UpdateTime {settings.UpdateTime}", -1);

        _settings = settings;
        
        Log($"Creating filestream", -1);

        _fileBuffer = new ConcurrentQueue<FileChunk>();

        Log($"Creating httpClient", -1);

        // The timeout is used in both doing the initial request and for timing out the stream.
        _httpClient = new HttpClient(new HttpClientHandler
        {
            MaxConnectionsPerServer = _settings.Parallel * 2
        });
        _httpClient.Timeout = TimeSpan.FromMilliseconds(Math.Max(30000, settings.Timeout));
        
        Log($"Constructor done", -1);
    }

    /// <summary>
    /// Download a file with the given url to the given path parameter.
    /// </summary>
    /// <param name="cancellationToken">The cancellationtoken lets you cancel the download completely.</param>
    /// <exception cref="OperationCanceledException">Occurs when the cancellation token is cancelled.</exception>
    /// <exception cref="TaskCanceledException">Occurs when the download cannot be started and is timed out.</exception>
    /// <exception cref="TimeoutException">Occurs when the download times out while downloading.</exception>
    public async Task Download(CancellationToken cancellationToken = default)
    {
        Log($"Download start", -1);

        // Fetch the content size from the URL, if the content size cannot be fetched,
        // we can only download with a single chunk.
        var contentSize = await GetContentSize(cancellationToken);

        if (_settings.ChunkSize == 0)
        {
            if (contentSize <= 1024 * 1024 * 10)
            {
                _settings.ChunkSize = 1024 * 1024 * 10;
            }
            else if (contentSize <= 1024 * 1024 * 100)
            {
                _settings.ChunkSize = 1024 * 1024 * 25;
            }
            else
            {
                _settings.ChunkSize = 1024 * 1024 * 50;
            }

            Log($"Setting chunk size to {_settings.ChunkSize}", -1);
        }

        if (_settings.ChunkSize < _settings.BufferSize)
        {
            _settings.ChunkSize = _settings.BufferSize;
        }

        if (contentSize == -1)
        {
            Log($"Force setting parallel to 1", -1);

            _settings.Parallel = 1;
        }
        else if (_settings.ChunkSize > contentSize)
        {
            Log($"Force setting chunk size to ${contentSize}", -1);

            _settings.ChunkSize = (Int32) contentSize;
            _settings.Parallel = 1;
        }

        Log($"Calculating chunks", -1);

        var chunks = new List<Chunk>();

        for (var startByte = 0L; startByte < contentSize; startByte += _settings.ChunkSize)
        {
            var endByte = Math.Min(startByte + _settings.ChunkSize, contentSize - 1);
            var length = endByte - startByte;

            Log($"Add chunk {startByte} - {endByte} ({length})", -1);

            chunks.Add(new Chunk
            {
                StartByte = startByte,
                EndByte = endByte,
                LengthBytes = length
            });
        }

        Log($"Chunks: {chunks.Count}", -1);

        _settings.Parallel = Math.Min(_settings.Parallel, chunks.Count);

        var completed = false;
        Exception? exception = null;

        // Open a filestream that allows multiple tasks writing to the stream simultaniously.
        // Use the semaphore to make sure only 1 task can write to the file at the same time
        // as we are using Seek to seek through the stream.
        var fileStream = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.None, _settings.BufferSize, true);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // ReSharper disable once MethodSupportsCancellation
        _ = Task.Run(async () =>
        {
            Log($"Starting download tasks with {_settings.Parallel} parallel downloads", -1, 1);

            try
            {
                var tasks = new List<Task>();

                for (var i = 0; i < chunks.Count; i++)
                {
                    while (!_fileBuffer.IsEmpty)
                    {
                        // ReSharper disable once MethodSupportsCancellation
                        await Task.Delay(1);
                    }

                    var chunk = chunks[i];

                    if (tasks.Count >= _settings.Parallel)
                    {
                        await AwaitAndProcessTask(tasks, cts);
                    }

                    tasks.Add(Download(i, chunk, cts.Token));
                }

                while (tasks.Count > 0)
                {
                    await AwaitAndProcessTask(tasks, cts);
                }

                cancellationToken.ThrowIfCancellationRequested();

                Log($"All tasks completed successful", -1, 1);
            }
            catch (AggregateException ex)
            {
                if (ex.InnerExceptions.Count == 1)
                {
                    exception = ex.InnerExceptions[0];
                }
                else
                {
                    var notCancelledNotTimeoutException = ex.InnerExceptions.FirstOrDefault(m => m is not TaskCanceledException && m is not TimeoutException);
                    exception = notCancelledNotTimeoutException;

                    if (exception == null)
                    {
                        var notCancelledNotException = ex.InnerExceptions.FirstOrDefault(m => m is not TaskCanceledException);
                        exception = notCancelledNotException;
                    }

                    exception ??= ex;
                }
            }
            catch (Exception ex)
            {
                Log($"Error with awaiting all tasks", -1);
                Log(ex, -1);
                exception = ex;
            }
            finally
            {
                completed = true;
                Log($"Complete", -1, 1);
            }
        });

        // Call the OnProgress event periodically and wait for all chunks to complete,
        // then call the OnComplete function.
        // ReSharper disable once MethodSupportsCancellation
        _ = Task.Run(async () =>
        {
            Log($"Start OnProgress runner", -1);

            while (!completed)
            {
                OnProgress?.Invoke(chunks, _fileBuffer.Count);

                // ReSharper disable once MethodSupportsCancellation
                await Task.Delay(_settings.UpdateTime);
            }

            OnProgress?.Invoke(chunks, _fileBuffer.Count);

            Log($"Waiting for file write to complete", -1);

            // Wait for the file buffer to finish writing
            // ReSharper disable once LoopVariableIsNeverChangedInsideLoop
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            while (fileStream != null)
            {
                // ReSharper disable once MethodSupportsCancellation
                await Task.Delay(_settings.UpdateTime);
            }

            Log($"Finished waiting for file write", -1);

            GC.Collect();

            OnComplete?.Invoke(contentSize, exception);

            Log($"End OnProgress runner", -1);
        });

        // Periodically write the queue to the disk.
        // ReSharper disable once MethodSupportsCancellation
        _ = Task.Run(async () =>
        {
            Log($"Start FileQueue runner", -1);

            while (!completed || _fileBuffer.Count > 0)
            {
                if (_fileBuffer.TryDequeue(out var fileChunk))
                {
                    fileStream.Seek(fileChunk.Position, SeekOrigin.Begin);

                    // ReSharper disable once MethodSupportsCancellation
                    await fileStream.WriteAsync(fileChunk.Buffer, 0, fileChunk.Length);

                    _bufferPool.Return(fileChunk.Buffer);
                }
                else
                {
                    // ReSharper disable once MethodSupportsCancellation
                    await Task.Delay(1);
                }
            }
            
            Log($"File writing complete", -1);

            fileStream.Close();
            fileStream = null;

            Log($"End FileQueue runner", -1);
        });

        // Because above tasks are running async the Download method will return immediatly.
    }

    /// <summary>
    /// Wait for a task to complete in the task list, if it's faulted, cancel the cancellation token which
    /// cancels all other tasks, then rethrow the exception.
    /// </summary>
    /// <param name="tasks">List of tasks to await</param>
    /// <param name="cts">The cancellation token source to cancel when a task is faulted.</param>
    /// <returns></returns>
    private async Task AwaitAndProcessTask(List<Task> tasks, CancellationTokenSource cts)
    {
        var completedTask = await Task.WhenAny(tasks);
        tasks.Remove(completedTask);

        if (_lastGc < DateTimeOffset.UtcNow)
        {
            _lastGc = DateTimeOffset.UtcNow.AddSeconds(10);
            GC.Collect();
        }

        if (completedTask.IsFaulted)
        {
            cts.Cancel();
            throw completedTask.Exception ?? new Exception("A task was faulted without throwing an exception");
        }
    }

    /// <summary>
    /// Download a chunk.
    /// </summary>
    /// <param name="thread">The index of the chunk.</param>
    /// <param name="chunk">The chunk to download.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the download.</param>
    /// <exception cref="TimeoutException">Occurs when the Settings.Timeout has reached and no data has come in since.</exception>
    private async Task Download(Int64 thread, Chunk chunk, CancellationToken cancellationToken)
    {
        var retry = 0;
        var complete = false;
        Exception? lastException = null;

        var startByte = chunk.StartByte;
        var endByte = chunk.EndByte;
        
        while (retry <= _settings.RetryCount && !complete && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                Log($"Start download from bytes {startByte} to {endByte}", thread);

                // Use the Range parameter to get only a part of the download.
                var request = new HttpRequestMessage(HttpMethod.Get, _uri);
                request.Headers.Range = new RangeHeaderValue(startByte, endByte);

                // Using ResponseHeadersRead gives more control of the result stream.
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var ex = new Exception($"Failed to download bytes from range {startByte}-{endByte}. Status code: {response.StatusCode}");
                    Log(ex, thread);

                    throw ex;
                }

                Log($"Start read stream", thread);

                using var stream = await response.Content.ReadAsStreamAsync();

                Log($"Finished read stream", thread);

                // Construct a task for the stream timeout.
                var timeoutCompletionSource = new TaskCompletionSource<Boolean>();
                using var timer = new Timer(_ => timeoutCompletionSource.TrySetResult(true));

                // Move the cancellationToken into a task completion source so that we can always
                // cancel the task, even if the stream is hanging.
                var cancellationCompletionSource = new TaskCompletionSource<Boolean>();
                cancellationToken.Register(() => cancellationCompletionSource.TrySetResult(true));

                var bandwidth = new Bandwidth(_settings.MaximumBytesPerSecond / _settings.Parallel);

                var totalBytesRead = 0L;

                var position = startByte;

                Log($"Start timeout timer", thread);

                // Start the timeout timer.
                timer.Change(TimeSpan.FromMilliseconds(_settings.Timeout), Timeout.InfiniteTimeSpan);

                // Keep reading until the download is completed, or a timeout occurs, or the cancellation token is cancelled.
                while (true)
                {
                    var tempBuffer = _bufferPool.Rent(_settings.BufferSize);

                    var bytesReadTask = stream.ReadAsync(tempBuffer, 0, tempBuffer.Length, cancellationToken);
                    var completedTask = await Task.WhenAny(bytesReadTask, timeoutCompletionSource.Task, cancellationCompletionSource.Task);

                    if (completedTask == timeoutCompletionSource.Task)
                    {
                        Log($"TimeoutException", thread);

                        throw new TimeoutException();
                    }

                    if (completedTask == cancellationCompletionSource.Task)
                    {
                        Log($"OperationCanceledException", thread);

                        throw new OperationCanceledException();
                    }

                    var bytesRead = 0;
                    try
                    {
                        bytesRead = await bytesReadTask;
                    }
                    catch (Exception ex)
                    {
                        if (!ex.Message.Contains("The response ended prematurely"))
                        {
                            throw;
                        }
                    }

                    // Reset the stream timeout timer.
                    timer.Change(TimeSpan.FromMilliseconds(_settings.Timeout), Timeout.InfiniteTimeSpan);

                    // If no more bytes are read in the stream the download is complete.
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    // Update statistics and calcuate current speed.
                    totalBytesRead += bytesRead;

                    bandwidth.CalculateSpeed(bytesRead);

                    var delayTime = bandwidth.PopSpeedRetrieveTime();

                    if (delayTime > 0)
                    {
                        // ReSharper disable once MethodSupportsCancellation
                        await Task.Delay(delayTime);
                    }
                    
                    chunk.Speed = bandwidth.Speed;
                    chunk.DownloadBytes = totalBytesRead;

                    if (chunk.LengthBytes > 0)
                    {
                        chunk.Progress = ((Double) totalBytesRead / chunk.LengthBytes) * 100;
                    }

                    // Write the results to the file queue to be written to disk later.
                    _fileBuffer.Enqueue(new FileChunk(position, bytesRead, tempBuffer));

                    position += bytesRead;

                    lastException = null;
                }

                complete = true;
            }
            catch (Exception ex)
            {
                Log($"Exception, retry {retry}", thread);
                Log(ex, thread);
                
                lastException = ex;

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                retry++;

                if (retry > 5)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(retry), cancellationToken);
                }
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            throw new TaskCanceledException();
        }

        if (complete)
        {
            chunk.Completed = true;
        }
        else if (lastException != null)
        {
            throw lastException;
        }
        else
        {
            throw new Exception("Unable to complete download");
        }
    }

    /// <summary>
    /// Get the content size of the given URI.
    /// </summary>
    /// <returns>The content size in bytes. Will return -1 if the server does not support getting the content size.</returns>
    /// <exception cref="Exception"></exception>
    private async Task<Int64> GetContentSize(CancellationToken cancellationToken)
    {
        Log($"GetContentSize start", -1);
        var responseHeaders = await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, _uri), cancellationToken);

        if (!responseHeaders.IsSuccessStatusCode)
        {
            var content = await responseHeaders.Content.ReadAsStringAsync();
            var ex = new Exception($"Unable to retrieve content size before downloading, received response: {responseHeaders.StatusCode} {content}");
            Log(ex, -1);

            throw ex;
        }

        var result = responseHeaders.Content.Headers.ContentLength ?? -1;
        Log($"Content size {result}", -1, 1);

        Log($"GetContentSize end", -1);

        return result;
    }

    private void Log(String message, Int64 chunk, Int32 logLevel = 0)
    {
        if (_settings == null || logLevel >= _settings.LogLevel)
        {
            OnLog?.Invoke(new LogMessage
                          {
                              Message = message,
                              Thread = chunk
                          }, logLevel);
        }
    }

    private void Log(Exception ex, Int64 chunk)
    {
        if (_settings == null || _settings.LogLevel >= 4)
        {
            OnLog?.Invoke(new LogMessage
                          {
                              Message = ex.Message,
                              Thread = chunk,
                              Exception = ex
                          }, 4);
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private class FileChunk
    {
        public FileChunk(Int64 position, Int32 length, Byte[] buffer)
        {
            Position = position;
            Length = length;
            Buffer = buffer;
        }

        public Int64 Position { get; }
        public Int32 Length { get; }
        public Byte[] Buffer { get; }
    }
}
