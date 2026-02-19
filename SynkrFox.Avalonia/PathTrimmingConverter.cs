using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SynkrFox.App
{
    public class PathTrimmingConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string path && !string.IsNullOrEmpty(path))
            {
                if (path.Length > 40)
                {
                    string root = System.IO.Path.GetPathRoot(path) ?? "";
                    string fileName = System.IO.Path.GetFileName(path);
                    
                    if (!string.IsNullOrEmpty(fileName))
                    {
                        string middle = "...";
                        int remaining = 40 - root.Length - middle.Length;
                        if (remaining > 0 && fileName.Length < remaining)
                        {
                            string endPart = path.Substring(path.Length - remaining);
                            return $"{root}{middle}{endPart}";
                        }
                        return $"{path.Substring(0, 15)}...{path.Substring(path.Length - 20)}";
                    }
                }
                return path;
            }
            return value;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
