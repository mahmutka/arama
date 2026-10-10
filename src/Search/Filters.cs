using Arama.Index;

namespace Arama.Search
{
    public enum FilterKind { All, Files, Folders, Documents, Images, Audio, Video, Archives, Programs }

    public static class Filters
    {
        public static readonly string[] DisplayNames =
            { "Tümü", "Dosyalar", "Klasörler", "Belgeler", "Resimler", "Ses", "Video", "Arşivler", "Programlar" };

        static readonly string[][] Extensions =
        {
            null, null, null,
            new[] { "doc", "docx", "pdf", "txt", "rtf", "odt", "xls", "xlsx", "ods", "csv", "ppt", "pptx", "odp", "md" },
            new[] { "jpg", "jpeg", "png", "gif", "bmp", "webp", "heic", "svg", "tif", "tiff", "ico", "raw" },
            new[] { "mp3", "wav", "flac", "aac", "ogg", "m4a", "wma", "opus" },
            new[] { "mp4", "mkv", "avi", "mov", "wmv", "webm", "flv", "m4v", "mpg", "mpeg" },
            new[] { "zip", "rar", "7z", "tar", "gz", "bz2", "xz", "iso", "cab" },
            new[] { "exe", "msi", "bat", "cmd", "ps1", "lnk", "com" },
        };

        public static bool Accept(FilterKind filter, FileIndex index, int id)
        {
            if (filter == FilterKind.All) return true;
            bool isDirectory = index.IsDirectory(id);
            if (filter == FilterKind.Folders) return isDirectory;
            if (isDirectory) return false;
            if (filter == FilterKind.Files) return true;
            return HasExtension(index.names, index.nameOffset[id], index.nameLength[id], Extensions[(int)filter]);
        }

        static bool HasExtension(char[] s, int offset, int length, string[] extensions)
        {
            int dot = -1;
            for (int i = length - 1; i >= 0; i--)
            {
                if (s[offset + i] == '.') { dot = i; break; }
            }
            if (dot < 0) return false;
            int extensionLength = length - dot - 1;
            foreach (string extension in extensions)
            {
                if (extension.Length != extensionLength) continue;
                int k = 0;
                while (k < extensionLength && TextFold.Fold(s[offset + dot + 1 + k]) == extension[k]) k++;
                if (k == extensionLength) return true;
            }
            return false;
        }
    }
}
