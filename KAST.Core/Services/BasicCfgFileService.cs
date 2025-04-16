using KAST.Data.Models;
using System.Text.RegularExpressions;

namespace KAST.Core.Services
{
    public class BasicCfgFileService
    {
        private readonly string _filePath;
        private readonly Dictionary<string, (string originalLine, string comment)> _lines = new();
        private FileSystemWatcher _watcher;
        private bool _suppressWatcher = false;
        public BasicConfig Settings { get; private set; } = new();
        public event Action OnUpdated;

        public BasicCfgFileService(string filePath)
        {
            _filePath = filePath;
            EnsureFileExists();
            _filePath = Path.GetFullPath(filePath);
            LoadFile();
            WatchFile();
        }

        public string RawFileContent
        {
            get => File.ReadAllText(_filePath);
            set
            {
                File.WriteAllText(_filePath, value);
                LoadFile(); // refresh parsed values
                OnUpdated?.Invoke();
            }
        }

        private void EnsureFileExists()
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(_filePath))
            {
                var defaultSettings = new BasicConfig();
                var lines = new List<string>();

                foreach (var prop in typeof(BasicConfig).GetProperties())
                {
                    var value = prop.GetValue(defaultSettings);
                    var formattedValue = value switch
                    {
                        float f => f.ToString("0.#####"),
                        double d => d.ToString("0.#####"),
                        _ => value.ToString()
                    };
                    lines.Add($"{prop.Name} = {formattedValue};");
                }

                File.WriteAllLines(_filePath, lines);
            }
        }

        private void LoadFile()
        {
            _lines.Clear();
            var lines = File.ReadAllLines(_filePath);

            var regex = new Regex(@"^(\w+)\s*=\s*(.*?);(?:\s*\/\/\s*(.*))?$");

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!regex.IsMatch(trimmed)) continue;

                var match = regex.Match(trimmed);
                var key = match.Groups[1].Value;
                var value = match.Groups[2].Value;
                var comment = match.Groups[3].Success ? match.Groups[3].Value : null;

                _lines[key] = (line, comment);

                SetSettingProperty(key, value);
            }
            OnUpdated?.Invoke();
        }

        private void SetSettingProperty(string key, string value)
        {
            try
            {
                var prop = typeof(BasicConfig).GetProperty(key);
                if (prop == null) return;

                object converted = Convert.ChangeType(value, prop.PropertyType);
                prop.SetValue(Settings, converted);
            }
            catch { }
        }

        private string ConvertSettingValue(string key)
        {
            var prop = typeof(BasicConfig).GetProperty(key);
            if (prop == null) return null;

            var val = prop.GetValue(Settings);
            return val switch
            {
                float f => f.ToString("0.#####"),
                double d => d.ToString("0.#####"),
                _ => val.ToString()
            };
        }

        public void UpdateFileFromSettings()
        {
            _suppressWatcher = true;
            var lines = new List<string>(File.ReadAllLines(_filePath));
            for (int i = 0; i < lines.Count; i++)
            {
                var match = Regex.Match(lines[i], @"^(\w+)\s*=\s*(.*?);(?:\s*\/\/\s*(.*))?$");
                if (!match.Success) continue;

                var key = match.Groups[1].Value;
                if (!_lines.ContainsKey(key)) continue;

                var newValue = ConvertSettingValue(key);
                var comment = match.Groups[3].Success ? $" // {match.Groups[3].Value}" : "";
                var newLine = $"{key} = {newValue};{comment}";
                lines[i] = newLine;
            }

            File.WriteAllLines(_filePath, lines);
            _suppressWatcher = false;
        }

        private void WatchFile()
        {
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(_filePath))
            {
                Filter = Path.GetFileName(_filePath),
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };

            _watcher.Changed += (s, e) =>
            {
                if (_suppressWatcher) return;
                Thread.Sleep(100); // give the system time to release file lock
                LoadFile();
            };

            _watcher.EnableRaisingEvents = true;
        }
    }
}
