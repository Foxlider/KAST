using System.Text.RegularExpressions;

namespace KAST.Core.Services
{
    public interface IConfigFormat<T>
    {
        T Parse(string content);
        string Serialize(T config);
    }

    public class ConfigFileService<T>
    {
        private readonly string _filePath;
        private readonly IConfigFormat<T> _format;
        private FileSystemWatcher _watcher;
        private bool _suppressWatcher = false;
        private readonly Lock fileLock = new();

        public T Config { get; private set; }
        public event Action OnUpdated;

        public ConfigFileService(string filePath, IConfigFormat<T> format)
        {
            _filePath = Path.GetFullPath(filePath);
            _format = format;

            EnsureFileExists();
            LoadFile();
            WatchFile();
        }

        public string RawFileContent
        {
            get => File.ReadAllText(_filePath);
            set
            {
                fileLock.Enter();
                File.WriteAllText(_filePath, value);
                fileLock.Exit();
                LoadFile();
            }
        }

        private void EnsureFileExists()
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (!File.Exists(_filePath))
                File.WriteAllText(_filePath, _format.Serialize(Activator.CreateInstance<T>()));
        }

        private void LoadFile()
        {
            var content = File.ReadAllText(_filePath);
            Config = _format.Parse(content);
            OnUpdated?.Invoke();
        }

        public void SaveFile()
        {
            _suppressWatcher = true;
            fileLock.Enter();
            //File.WriteAllText(_filePath, _format.Serialize(Config));
            try
            {
                var lines = File.ReadAllLines(_filePath);
                var updatedLines = new List<string>();
                var regex = new Regex(@"^(\w+)\s*=\s*(.*?);(?:\s*//\s*(.*))?$");

                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();

                    if (regex.IsMatch(trimmedLine))
                    {
                        var match = regex.Match(trimmedLine);
                        var key = match.Groups[1].Value;
                        var currentValue = match.Groups[2].Value;
                        var comment = match.Groups[3].Success ? match.Groups[3].Value : null;

                        var prop = typeof(T).GetProperty(key);
                        if (prop != null)
                        {
                            var configValue = prop.GetValue(Config);
                            var formattedValue = configValue switch
                            {
                                float f => f.ToString("0.#####"),
                                double d => d.ToString("0.#####"),
                                _ => configValue?.ToString()
                            };

                            if (formattedValue != currentValue)
                            {
                                var newLine = $"{key} = {formattedValue};";
                                if (!string.IsNullOrEmpty(comment))
                                {
                                    newLine += $" // {comment}";
                                }
                                updatedLines.Add(newLine);
                                continue;
                            }
                        }
                    }

                    updatedLines.Add(line);
                }

                File.WriteAllLines(_filePath, updatedLines);
            }
            finally
            {
                fileLock.Exit();
                _suppressWatcher = false;
            }
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
                if (_suppressWatcher || fileLock.IsHeldByCurrentThread) return;
                fileLock.Enter();
                Thread.Sleep(100);
                LoadFile();
                fileLock.Exit();
            };

            _watcher.EnableRaisingEvents = true;
        }
    }


    public class KeyValueConfigFormat<T> : IConfigFormat<T> where T : new()
    {
        private readonly Regex regex = new Regex(@"^(\w+)\s*=\s*(.*?);(?:\s*\/\/\s*(.*))?$", RegexOptions.Compiled);

        public T Parse(string content)
        {
            var result = new T();
            var lines = content.Split("\n");

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!regex.IsMatch(trimmed)) continue;

                var match = regex.Match(trimmed);
                var key = match.Groups[1].Value;
                var val = match.Groups[2].Value;
                var comment = match.Groups[3].Success ? match.Groups[3].Value : null;

                var prop = typeof(T).GetProperty(key);
                if (prop != null)
                {
                    try
                    {
                        object converted = Convert.ChangeType(val, prop.PropertyType);
                        prop.SetValue(result, converted);
                    }
                    catch { /* ignore parse errors */ }
                }
            }
            return result;
        }

        public string Serialize(T config)
        {
            var lines = new List<string>();
            foreach (var prop in typeof(T).GetProperties())
            {
                var value = prop.GetValue(config);
                var formatted = value switch
                {
                    float f => f.ToString("0.#####"),
                    double d => d.ToString("0.#####"),
                    _ => value?.ToString()
                };
                lines.Add($"{prop.Name} = {formatted};");
            }
            return string.Join(Environment.NewLine, lines);
        }
    }

    public class ClassHierarchyConfigFormat<T> : IConfigFormat<T> where T : new()
    {
        private static readonly Regex ClassRegex = new(@"class\s+(\w+)\s*\{([\s\S]*?)\};", RegexOptions.Compiled);
        private static readonly Regex PropRegex = new(@"(\w+)\s*=\s*([^;]+);(?:\s*\/\/\s*(.*))?", RegexOptions.Compiled);

        public T Parse(string content)
        {
            return (T)ParseClass(typeof(T), content);
        }

        private object ParseClass(Type type, string content)
        {
            var instance = Activator.CreateInstance(type);

            // Properties
            foreach (Match match in PropRegex.Matches(content))
            {
                var propName = match.Groups[1].Value;
                var rawValue = match.Groups[2].Value.Trim();

                var prop = type.GetProperty(propName);
                if (prop == null) continue;

                try
                {
                    object value = Convert.ChangeType(rawValue, prop.PropertyType);
                    prop.SetValue(instance, value);
                }
                catch { }
            }

            // Nested Classes
            foreach (Match classMatch in ClassRegex.Matches(content))
            {
                var nestedName = classMatch.Groups[1].Value;
                var nestedBody = classMatch.Groups[2].Value;

                var prop = type.GetProperty(nestedName);
                if (prop == null) continue;

                var nestedValue = ParseClass(prop.PropertyType, nestedBody);
                prop.SetValue(instance, nestedValue);
            }

            return instance;
        }

        public string Serialize(T config)
        {
            return SerializeClass(config, typeof(T), 0);
        }

        private string SerializeClass(object obj, Type type, int indentLevel)
        {
            var indent = new string('\t', indentLevel);
            var lines = new List<string>();

            foreach (var prop in type.GetProperties())
            {
                var value = prop.GetValue(obj);
                if (value == null) continue;

                if (prop.PropertyType.IsClass && prop.PropertyType != typeof(string))
                {
                    lines.Add($"{indent}class {prop.Name}");
                    lines.Add($"{indent}{{");
                    lines.Add(SerializeClass(value, prop.PropertyType, indentLevel + 1));
                    lines.Add($"{indent}}};");
                }
                else
                {
                    var formatted = value switch
                    {
                        float f => f.ToString("0.#####"),
                        double d => d.ToString("0.#####"),
                        _ => value.ToString()
                    };
                    lines.Add($"{indent}{prop.Name} = {formatted};");
                }
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
