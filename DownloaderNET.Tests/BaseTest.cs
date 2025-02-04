using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DownloaderNET.Tests;

public class BaseTest
{
    private String? _root;
    private Int32 _randomPort;

    private void StartServer(Options options)
    {
        var retries = new Dictionary<String, Int32>();

        var builder = WebApplication.CreateBuilder();

        var app = builder.Build();
        
        _root = Path.Combine(app.Environment.ContentRootPath, "test");

        if (!Directory.Exists(_root))
        {
            Directory.CreateDirectory(_root);
        }

        _randomPort = new Random().Next(6000, 7000);

        app.UseDeveloperExceptionPage();

        app.UseRouting();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapMethods("/", new[] { "GET", "HEAD" }, 
                             async context =>
                             {
                                 Int32? rangeFrom = null;
                                 Int32? rangeTo = null;
                                 String? key = null;

                                 if (context.Request.Method == "HEAD")
                                 {
                                     retries = new Dictionary<String, Int32>();

                                     var sb = new StringBuilder();
                                     for (var i = 1; i <= options.ServerSize; i++)
                                     {
                                         sb.AppendLine($"{i}\tabcdefghijklmnopqrstuvwxyz");
                                     }

                                     await File.WriteAllTextAsync(Path.Combine(_root, "test.bin"), sb.ToString());
                                 }
                                 else
                                 {
                                     var ct = context.Request.GetTypedHeaders();
                                     rangeFrom = (Int32?) ct.Range!.Ranges.First().From;
                                     rangeTo = (Int32?) ct.Range!.Ranges.First().To;

                                     key = $"{rangeFrom}-{rangeTo}";

                                     retries.TryAdd(key, 0);
                                 }

                                 if (options.ServerRequestTimeout > 0)
                                 {
                                     await Task.Delay(options.ServerRequestTimeout);
                                 }
                                 
                                 var file = await File.ReadAllBytesAsync(Path.Combine(_root, "test.bin"));

                                 if (rangeFrom.HasValue && rangeTo.HasValue)
                                 {
                                     file = file.AsSpan(rangeFrom.Value, rangeTo.Value - rangeFrom.Value).ToArray();
                                 }

                                 var fileStream = new MemoryStream();
                                 fileStream.Write(file, 0, file.Length);
                                 fileStream.Seek(0, SeekOrigin.Begin);

                                 var result = new FileCallbackResult(new MediaTypeHeaderValue("application/octet-stream"),
                                                                     async (outputStream, _) =>
                                                                     {
                                                                         var buffer = new Byte[4096];
                                                                         Int32 bytesRead;
                                                                         Int64 totalBytesRead = rangeFrom ?? 0;
                                                                         
                                                                         while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                                                                         {
                                                                             totalBytesRead += bytesRead;

                                                                             if (context.Request.Method == "GET")
                                                                             {
                                                                                 if (options.ServerFailAfterBytes > 0 && totalBytesRead > options.ServerFailAfterBytes)
                                                                                 {
                                                                                     if (retries[key!] < 2)
                                                                                     {
                                                                                         retries[key!]++;

                                                                                         throw new Exception("Test");
                                                                                     }
                                                                                 }

                                                                                 if (options.ServerStreamTimeout > 0 && totalBytesRead > 4096)
                                                                                 {
                                                                                     await Task.Delay(options.ServerStreamTimeout);
                                                                                 }
                                                                             }

                                                                             await outputStream.WriteAsync(buffer, 0, bytesRead);
                                                                         }

                                                                         await outputStream.FlushAsync();
                                                                     }, fileStream.Length);

                                 await result.ExecuteResultAsync(new ActionContext
                                 {
                                     HttpContext = context
                                 });
                             });
        });
        
        Task.Run(() =>
        {
            app.Logger.Log(LogLevel.Information, "Starting server");
            app.Run($"http://localhost:{_randomPort}");
        });
    }

    protected async Task<String> Download(Options options, CancellationToken cancellationToken = default)
    {
        StartServer(options);

        await Task.Delay(10);

        var fn = Path.Combine(_root!, $"result.bin");

        var tcs = new TaskCompletionSource<Boolean>();
        Exception? exception = null;

        var downloader = new Downloader($"http://localhost:{_randomPort}/", fn, new Settings
        {
            RetryCount = options.DownloaderRetryCount,
            Parallel = options.Parallel,
            Timeout = options.DownloaderTimeout,
            ChunkSize = (Int32) Math.Ceiling(328894.0 / options.Parallel)
        });
        
        downloader.OnComplete += (_, ex) =>
        {
            exception = ex;
            tcs.SetResult(true);
            return Task.CompletedTask;
        };

        await downloader.Download(cancellationToken);

        await tcs.Task;

        if (exception != null)
        {
            throw exception;
        }

        return GetHash(fn);
    }

    protected String GetHash(String fileName)
    {
        using var SHA256 = System.Security.Cryptography.SHA256.Create();
        using var fileStream = File.OpenRead(Path.Combine(_root!, fileName));
        return BitConverter.ToString(SHA256.ComputeHash(fileStream)).Replace("-", "").ToLowerInvariant();
    }

    private class FileCallbackResult : FileResult
    {
        private readonly Func<Stream, ActionContext, Task> _callback;
        private readonly Int64 _length;

        public FileCallbackResult(MediaTypeHeaderValue contentType, Func<Stream, ActionContext, Task> callback, Int64 length)
            : base(contentType.ToString())
        {
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
            _length = length;
        }

        public override Task ExecuteResultAsync(ActionContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var executor = new FileCallbackResultExecutor(context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>(), _length);

            return executor.ExecuteAsync(context, this);
        }

        private sealed class FileCallbackResultExecutor : FileResultExecutorBase
        {
            private readonly Int64 _length;

            public FileCallbackResultExecutor(ILoggerFactory loggerFactory, Int64 length)
                : base(CreateLogger<FileCallbackResultExecutor>(loggerFactory))
            {
                _length = length;
            }

            public Task ExecuteAsync(ActionContext context, FileCallbackResult result)
            {
                SetHeadersAndLog(context, result, _length, false);
                return result._callback(context.HttpContext.Response.Body, context);
            }
        }
    }
}

public class Options
{
    public Int32 Parallel { get; init; } = 1;

    public Int32 DownloaderRetryCount { get; init; }

    public Int32 DownloaderTimeout { get; init; } = 5000;

    public Int32 ServerSize { get; init; } = 10000;

    public Int32 ServerFailAfterBytes { get; init; }

    public Int32 ServerRequestTimeout { get; init; }

    public Int32 ServerStreamTimeout { get; init; }
}