namespace DownloaderNET;

public static class Helpers
{
    public static String ToMemoryMensurableUnit(this Double bytes)
    {
        var kb = bytes / 1024; // · 1024 Bytes = 1 Kilobyte 
        var mb = kb / 1024; // · 1024 Kilobytes = 1 Megabyte 
        var gb = mb / 1024; // · 1024 Megabytes = 1 Gigabyte 
        var tb = gb / 1024; // · 1024 Gigabytes = 1 Terabyte 

        var result =
            tb > 1 ? $"{tb:0.##}TB" :
            gb > 1 ? $"{gb:0.##}GB" :
            mb > 1 ? $"{mb:0.##}MB" :
            kb > 1 ? $"{kb:0.##}KB" :
            $"{bytes:0.##}B";

        result = result.Replace("/", ".");

        return result;
    }
}
