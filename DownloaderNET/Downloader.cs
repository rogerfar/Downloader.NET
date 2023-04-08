using System;
using System.Net.Http.Headers;
using System.Runtime;

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
    public Action<IList<ChunkProgress>>? OnProgress { get; set; }

    /**
     * The OnComplete will fire when all chunks are finished and the file has been written to disk completely.
     * The Exception parameter will be passed if an exception occurred during downloading.
     */
    public Func<Exception?, Task>? OnComplete { get; set; }

    private readonly Uri _uri;
    private readonly Settings _settings;
    private readonly FileStream _fileStream;
    private readonly SemaphoreSlim _fileSemaphore;

    private readonly IList<ChunkProgress> _chunks = new List<ChunkProgress>();

    private CancellationToken _cancellationToken;
    
    /// <summary>
    /// Construct the Downloader.
    /// </summary>
    /// <param name="url">The URL to download the file from.</param>
    /// <param name="path">The complete path (directory + filename) to write the file to.</param>
    /// <param name="settings">Pass additional _settings to the downloader.</param>
    /// <param name="cancellationToken">The cancellationtoken lets you cancel the download completely.</param>
    public Downloader(String url, String path, Settings? settings = null, CancellationToken cancellationToken = default)
    {
        _uri = new Uri(url);

        settings ??= new Settings
        {
            BufferSize = 4096,
            ChunkCount = 1,
            MaximumBytesPerSecond = 0,
            Timeout = 30000,
            RetryCount = 0,
            UpdateTime = 1000
        };

        if (settings.BufferSize <= 0)
        {
            settings.BufferSize = 4096;
        }

        if (settings.ChunkCount <= 0)
        {
            settings.ChunkCount = 1;
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

        if (settings.UpdateTime < 0)
        {
            settings.UpdateTime = 1000;
        }

        _settings = settings;
        _cancellationToken = cancellationToken;

        // Open a filestream that allows multiple tasks writing to the stream simultaniously.
        // Use the semaphore to make sure only 1 task can write to the file at the same time
        // as we are using Seek to seek through the stream.
        _fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, _settings.BufferSize, true);
        _fileSemaphore = new SemaphoreSlim(1);
    }

    /// <summary>
    /// Download a file with the given url to the given path parameter.
    /// </summary>
    /// <exception cref="OperationCanceledException">Occurs when the cancellation token is cancelled.</exception>
    /// <exception cref="TaskCanceledException">Occurs when the download cannot be started and is timed out.</exception>
    /// <exception cref="TimeoutException">Occurs when the download times out while downloading.</exception>

    public async Task Download()
    {
        // Fetch the content size from the URL, if the content size cannot be fetched,
        // we can only download with a single chunk.
        var contentSize = await GetContentSize();

        if (contentSize == -1)
        {
            _settings.ChunkCount = 1;
        }
        
        var tasks = new List<Task>();

        // Determine the size of each chunk based on the given chunk count.
        var chunkSize = contentSize / _settings.ChunkCount;

        // If the chunk count is smaller than the buffer size, reduce the chunk count and re-calculate.
        // Downloading smaller than the buffer gives unexpected results and doesn't make any sense.
        while (chunkSize < _settings.BufferSize && _settings.ChunkCount > 1)
        {
            _settings.ChunkCount--;
            chunkSize = contentSize / _settings.ChunkCount;
        }

        // Divide the max speed by the chunk count so that all chunks together download with the target speed.
        _settings.MaximumBytesPerSecond /= _settings.ChunkCount;

        for (var i = 0; i < _settings.ChunkCount; i++)
        {
            // Calcuate the position of each chunk it needs to download, make sure the last chunk downloads the remainder.
            var startByte = i * (contentSize / _settings.ChunkCount);
            var endByte = i == _settings.ChunkCount - 1 ? contentSize : startByte + chunkSize;

            _chunks.Add(new ChunkProgress
            {
                StartByte = startByte,
                EndByte = endByte,
                LengthBytes = endByte - startByte
            });

            tasks.Add(Download(i, startByte, endByte, 0));
        }

        var completed = false;
        Exception? exception = null;

        // Start all tasks and wait for them all toe complete.
        // If an exception occurs, store it for later use, but make sure the fileSteam always closes.
        // ReSharper disable once MethodSupportsCancellation
        _= Task.Run(async () =>
        {
            try
            {
                // Unfortunately WhenAll blocks and waits for ALL tasks to complete.
                // If a single chunk errors out, it will still wait for all the other chunks to complete.
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                _fileStream.Close();
                completed = true;
            }
        });

        // Call the OnProgress event periodically and wait for all chunks to complete,
        // then call the OnComplete function.
        // ReSharper disable once MethodSupportsCancellation
        _ = Task.Run(async () =>
        {
            while (!completed)
            {
                OnProgress?.Invoke(_chunks);

                // ReSharper disable once MethodSupportsCancellation
                await Task.Delay(_settings.UpdateTime);
            }

            OnProgress?.Invoke(_chunks);

            OnComplete?.Invoke(exception);
        });

        // Because above 2 tasks are running async the Download method will return immediatly.
    }

    /// <summary>
    /// Download a chunk.
    /// </summary>
    /// <param name="thread">The index of the chunk.</param>
    /// <param name="startByte">The start byte position of the stream to read.</param>
    /// <param name="endByte">The end byte position of the stream to read to.</param>
    /// <param name="attempt">The attempt indicates recursively which attempt number this download is.</param>
    /// <exception cref="TimeoutException">Occurs when the Settings.Timeout has reached and no data has come in since.</exception>
    private async Task Download(Int32 thread, Int64 startByte, Int64 endByte, Int32 attempt)
    {
        try
        {
            // The timeout is used in both doing the initial request and for timing out the stream.
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMilliseconds(_settings.Timeout);

            // Use the Range parameter to get only a part of the download.
            var request = new HttpRequestMessage(HttpMethod.Get, _uri);
            request.Headers.Range = new RangeHeaderValue(startByte, endByte);

            // Using ResponseHeadersRead gives more control of the result stream.
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to download bytes from range {startByte}-{endByte}. Status code: {response.StatusCode}");
            }

            using var stream = await response.Content.ReadAsStreamAsync();

            // Construct a task for the stream timeout.
            var timeoutCompletionSource = new TaskCompletionSource<Boolean>();
            using var timer = new Timer(_ => timeoutCompletionSource.TrySetResult(true));

            // Move the cancellationToken into a task completion source so that we can always
            // cancel the task, even if the stream is hanging.
            var cancellationCompletionSource = new TaskCompletionSource<Boolean>();
            _cancellationToken.Register(() => cancellationCompletionSource.TrySetResult(true));

            var buffer = new Byte[_settings.BufferSize];

            var totalBytesRead = 0L;

            var position = startByte;

            // Use a time variable to keep track of how much time has passed since start downloading.
            // This is used for speed calculations.
            var startTime = DateTime.UtcNow;

            // Start the timeout timer.
            timer.Change(TimeSpan.FromMilliseconds(_settings.Timeout), Timeout.InfiniteTimeSpan);

            // Keep reading until the download is completed, or a timeout occurs, or the cancellation token is cancelled.
            while (true)
            {
                var bytesReadTask = stream.ReadAsync(buffer, 0, buffer.Length, _cancellationToken);
                var completedTask = await Task.WhenAny(bytesReadTask, timeoutCompletionSource.Task, cancellationCompletionSource.Task);

                if (completedTask == timeoutCompletionSource.Task)
                {
                    throw new TimeoutException();
                }

                if (completedTask == cancellationCompletionSource.Task)
                {
                    throw new OperationCanceledException();
                }

                var bytesRead = await bytesReadTask;

                // If no more bytes are read in the stream the download is complete.
                if (bytesRead == 0)
                {
                    break;
                }

                // Update statistics and calcuate current speed.
                totalBytesRead += bytesRead;

                var elapsedTime = DateTime.UtcNow - startTime;
                var downloadSpeed = totalBytesRead / elapsedTime.TotalSeconds;
                _chunks[thread].Speed = downloadSpeed;
                _chunks[thread].DownloadBytes = totalBytesRead;

                if (_chunks[thread].LengthBytes > 0)
                {
                    _chunks[thread].Progress = ((Double)totalBytesRead / (Double)_chunks[thread].LengthBytes) * 100;
                }

                // Write the result of the buffer to the disk by setting the lock so no other
                // chunks can write at the same time, seek in the stream to the current position,
                // then write the buffer to the file stream.
                await _fileSemaphore.WaitAsync(_cancellationToken);

                try
                {
                    _fileStream.Seek(position, SeekOrigin.Begin);

                    // ReSharper disable once MethodSupportsCancellation
                    await _fileStream.WriteAsync(buffer, 0, bytesRead);
                    position += bytesRead;
                }
                finally
                {
                    _fileSemaphore.Release();
                }

                // If the speed limiter is set and we are over the download speeed, calculate how much 
                // of a time delay we need to introduce to rate limit the reading of the stream.
                if (_settings.MaximumBytesPerSecond > 0 && downloadSpeed > _settings.MaximumBytesPerSecond)
                {
                    var delay = TimeSpan.FromSeconds(bytesRead / (Double)_settings.MaximumBytesPerSecond);

                    // ReSharper disable once MethodSupportsCancellation
                    await Task.Delay(delay);
                }
                
                // Reset the stream timeout timer.
                timer.Change(TimeSpan.FromMilliseconds(_settings.Timeout), Timeout.InfiniteTimeSpan);
            }

            _chunks[thread].Completed = true;
        }
        catch (IOException ex)
        {
            // In rare occassions webservers don't report the content length the same as the range length.
            // The only thing we can do is ignore this error.
            if (!ex.Message.StartsWith("The response ended prematurely"))
            {
                throw;
            }
        }
        catch (Exception)
        {
            // When an exception occurs make sure to rethrow when the cancellationtoken is cancelled, and don't retry.
            // Otherwise retry to download with the same parameters.
            if (_cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            if (attempt < _settings.RetryCount)
            {
                attempt += 1;
                await Download(thread, startByte, endByte, attempt);
            }
            else
            {
                throw;
            }
        }
    }

    /// <summary>
    /// Get the content size of the given URI.
    /// </summary>
    /// <returns>The content size in bytes. Will return -1 if the server does not support getting the content size.</returns>
    /// <exception cref="Exception"></exception>
    private async Task<Int64> GetContentSize()
    {
        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(_settings.Timeout)
        };

        var responseHeaders = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, _uri), _cancellationToken);

        if (!responseHeaders.IsSuccessStatusCode)
        {
            var content = await responseHeaders.Content.ReadAsStringAsync();
            throw new Exception($"Unable to retrieve content size before downloading, received response: {responseHeaders.StatusCode} {content}");
        }

        return responseHeaders.Content.Headers.ContentLength ?? -1;
    }

    public void Dispose()
    {
        _fileStream.Dispose();
        _fileSemaphore.Dispose();
    }
}
