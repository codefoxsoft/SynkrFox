using System;
using System.Globalization;
using System.Windows.Data;

namespace SynkrFox
{
    public class PathTrimmingConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string path && !string.IsNullOrEmpty(path))
            {
                if (path.Length > 40)
                {
                    string root = System.IO.Path.GetPathRoot(path) ?? "";
                    string fileName = System.IO.Path.GetFileName(path);
                    
                    // Simple compact: C:\...\Folder
                    if (!string.IsNullOrEmpty(fileName))
                    {
                        string middle = "...";
                        // Try to keep as much of the end as possible
                        int remaining = 40 - root.Length - middle.Length;
                        if (remaining > 0 && fileName.Length < remaining)
                        {
                            // If user has a very deep path but short filename, let's try to include some parent dir?
                            // For simplicity, let's just do Root + ... + LastPartOfPath
                            // Actually, standard compact path often does Start...End
                            
                            string endPart = path.Substring(path.Length - remaining);
                            return $"{root}{middle}{endPart}";
                        }
                        
                        // Fallback
                        return $"{path.Substring(0, 15)}...{path.Substring(path.Length - 20)}";
                    }
                }
                return path;
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
