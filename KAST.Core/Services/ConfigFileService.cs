using KAST.Core.Helpers;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace KAST.Core.Services
{
    public interface IConfigFormat<T>
    {
        T Parse(string content);
        string Serialize(T config);
    }

    #region Config File Service
    public class ConfigFileService<T> : IDisposable where T : new()
    {
        private readonly string _filePath;
        private readonly IConfigFormat<T> _format;
        private FileSystemWatcher? _watcher;
        private bool _suppressWatcher;
        private Timer? _debounceTimer;
        private readonly Lock _lock = new();
        private static readonly ActivitySource ActivitySource = new("KAST.Core.ConfigFileService");

        public T Config { get; private set; } = default!;
        public event Action? OnUpdated;

        public ConfigFileService(string filePath, IConfigFormat<T> format)
        {
            _filePath = Path.GetFullPath(filePath);
            _format = format;

            using var activity = ActivitySource.StartActivity("ConfigFileService.Initialize");
            activity?.SetTag("file.path", _filePath);
            activity?.SetTag("config.type", typeof(T).Name);

            EnsureFileExists();
            LoadFile();
            WatchFile();

            activity?.SetStatus(ActivityStatusCode.Ok);
        }

        public string RawFileContent
        {
            get => File.ReadAllText(_filePath);
            set
            {
                using var activity = ActivitySource.StartActivity("ConfigFileService.SetRawContent");
                activity?.SetTag("file.path", _filePath);
                activity?.SetTag("content.length", value?.Length ?? 0);

                try
                {
                    lock (_lock)
                    {
                        File.WriteAllText(_filePath, value);
                    }
                    LoadFile();
                    
                    activity?.SetStatus(ActivityStatusCode.Ok);
                }
                catch (Exception ex)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    throw;
                }
            }
        }

        private void EnsureFileExists()
        {
            using var activity = ActivitySource.StartActivity("ConfigFileService.EnsureFileExists");
            activity?.SetTag("file.path", _filePath);

            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                var fileExists = File.Exists(_filePath);
                var dirExists = Directory.Exists(dir);

                activity?.SetTag("file.exists", fileExists);
                activity?.SetTag("directory.exists", dirExists);

                if (string.IsNullOrEmpty(dir)) 
                    throw new InvalidOperationException("Invalid file path");

                if (!dirExists) 
                {
                    Directory.CreateDirectory(dir);
                    activity?.SetTag("directory.created", true);
                }

                if (!fileExists)
                {
                    File.WriteAllText(_filePath, _format.Serialize(new T()));
                    activity?.SetTag("file.created", true);
                }

                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw;
            }
        }

        private void LoadFile()
        {
            using var activity = ActivitySource.StartActivity("ConfigFileService.LoadFile");
            activity?.SetTag("file.path", _filePath);

            try
            {
                var content = File.ReadAllText(_filePath);
                activity?.SetTag("content.length", content.Length);
                
                Config = _format.Parse(content);
                OnUpdated?.Invoke();
                
                activity?.SetTag("load.success", true);
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.SetTag("load.success", false);
                
                Debug.WriteLine($"Config load failed: {ex.Message}");
                throw;
            }
        }

        public void SaveFile()
        {
            using var activity = ActivitySource.StartActivity("ConfigFileService.SaveFile");
            activity?.SetTag("file.path", _filePath);

            try
            {
                _suppressWatcher = true;
                
                string serializedContent;
                lock (_lock)
                {
                    serializedContent = _format.Serialize(Config);
                    File.WriteAllText(_filePath, serializedContent);
                }

                activity?.SetTag("content.length", serializedContent.Length);
                activity?.SetTag("save.success", true);
                activity?.SetStatus(ActivityStatusCode.Ok);

                // Delay un-suppress to let FS settle
                Task.Delay(300).ContinueWith(_ => _suppressWatcher = false);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.SetTag("save.success", false);
                throw;
            }
        }

        private void WatchFile()
        {
            using var activity = ActivitySource.StartActivity("ConfigFileService.SetupFileWatcher");
            activity?.SetTag("file.path", _filePath);

            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (string.IsNullOrEmpty(directory))
                    throw new InvalidOperationException("Invalid file path - no directory");

                _watcher = new FileSystemWatcher(directory)
                {
                    Filter = Path.GetFileName(_filePath),
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
                };

                _watcher.Changed += (s, e) =>
                {
                    if (_suppressWatcher) return;
                    
                    using var changeActivity = ActivitySource.StartActivity("ConfigFileService.FileChanged");
                    changeActivity?.SetTag("file.path", _filePath);
                    
                    _debounceTimer?.Dispose();
                    _debounceTimer = new Timer(_ =>
                    {
                        lock (_lock)
                        {
                            LoadFile();
                        }
                    }, null, 200, Timeout.Infinite);

                    changeActivity?.SetStatus(ActivityStatusCode.Ok);
                };

                _watcher.EnableRaisingEvents = true;
                activity?.SetTag("watcher.enabled", true);
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            _watcher?.Dispose();
            _debounceTimer?.Dispose();
        }
    }
    #endregion

    #region Syntax Tree
    public abstract class ConfigNode 
    { 
        public string RawText = string.Empty; 
    }

    class KeyValueNode : ConfigNode 
    { 
        public string Key = string.Empty; 
        public string Value = string.Empty; 
        public string Comment = string.Empty; 
    }

    class ClassNode : ConfigNode 
    { 
        public string Name = string.Empty; 
        public List<ConfigNode> Children = []; 
    }

    class CommentNode : ConfigNode { }
    class WhitespaceNode : ConfigNode { }
    #endregion

    #region Parser
    public static partial class ConfigParser
    {
        private static readonly Regex KeyValueRegex = RegexKeyValue();
        private static readonly Regex ClassNameRegex = RegexClassName();
        private static readonly Regex ClassEndRegex = RegexClassEnd();

        public static List<ConfigNode> Parse(string content)
        {
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int idx = 0;
            return ParseLines(lines, ref idx);
        }

        private static List<ConfigNode> ParseLines(string[] lines, ref int index)
        {
            var nodes = new List<ConfigNode>();


            while (index < lines.Length)
            {
                var rawLine = lines[index];
                var line = rawLine.Trim();

                if (string.IsNullOrWhiteSpace(line))
                {
                    nodes.Add(new WhitespaceNode { RawText = rawLine });
                    index++;
                    continue;
                }

                if (line.StartsWith("//"))
                {
                    nodes.Add(new CommentNode { RawText = rawLine });
                    index++;
                    continue;
                }


                var kvMatch = KeyValueRegex.Match(line);
                if (kvMatch.Success)
                {
                    var kvNode = new KeyValueNode
                    {
                        RawText = rawLine,
                        Key = kvMatch.Groups[1].Value,
                        Value = kvMatch.Groups[2].Value.Trim(),
                        Comment = kvMatch.Groups[3].Success ? kvMatch.Groups[3].Value.Trim() : null
                    };


                    nodes.Add(kvNode);
                    index++;
                    continue;
                }


                var clsMatch = ClassNameRegex.Match(line);
                if (clsMatch.Success)
                {
                    var clsNode = new ClassNode { Name = clsMatch.Groups[1].Value, RawText = rawLine };
                    index++;


                    // Skip lines until opening brace
                    while (index < lines.Length && lines[index].Trim() != "{")
                    {
                        if (!string.IsNullOrWhiteSpace(lines[index]))
                            clsNode.Children.Add(new WhitespaceNode { RawText = lines[index] });
                        index++;
                    }


                    // Skip the brace itself
                    if (index < lines.Length && lines[index].Trim() == "{") index++;
                    clsNode.Children.AddRange(ParseLines(lines, ref index));
                    nodes.Add(clsNode);
                    continue;
                }


                if (ClassEndRegex.IsMatch(line))
                {
                    index++;
                    break;
                }


                nodes.Add(new WhitespaceNode { RawText = rawLine });
                index++;
            }


            return nodes;
        }

        public static string Serialize(List<ConfigNode> nodes, int indentLevel = 0)
        {
            var lines = new List<string>();
            var indent = new string('\t', indentLevel);


            foreach (var node in nodes)
            {
                switch (node)
                {
                    case KeyValueNode kv:
                        var line = $"{indent}{kv.Key} = {kv.Value};";
                        if (!string.IsNullOrEmpty(kv.Comment))
                            line += $" // {kv.Comment}";
                        lines.Add(line);
                        break;
                    case ClassNode cls:
                        lines.Add($"{indent}class {cls.Name}");
                        lines.Add($"{indent}{{");
                        lines.Add(Serialize(cls.Children, indentLevel + 1));
                        lines.Add($"{indent}}};");
                        break;
                    default:
                        lines.Add(indent + node.RawText);
                        break;
                }
            }
            return string.Join("\n", lines);
        }

        [GeneratedRegex(@"^(\w+)\s*=\s*(.*?)\s*;\s*(?:\/\/\s*(.*))?$", RegexOptions.Compiled)]
        private static partial Regex RegexKeyValue();

        [GeneratedRegex(@"^class\s+(\w+)", RegexOptions.Compiled)]
        private static partial Regex RegexClassName();

        [GeneratedRegex(@"^\s*\};\s*$", RegexOptions.Compiled)]
        private static partial Regex RegexClassEnd();
    }
    #endregion

    #region Config Format
    public class Arma3ConfigFormat<T> : IConfigFormat<T> where T : new()
    {
        private List<ConfigNode> _nodes = new();

        public T Parse(string content)
        {
            _nodes = ConfigParser.Parse(content);
            var result = new T();


            foreach (var node in _nodes)
            {
                ApplyNodeToObject(node, result);
            }
            return result;
        }

        private void ApplyNodeToObject(ConfigNode node, object obj)
        {
            if (node is KeyValueNode kv)
            {
                var prop = obj.GetType().GetProperty(kv.Key);
                if (prop != null)
                {
                    try
                    {
                        var converted = Convert.ChangeType(kv.Value, prop.PropertyType);
                        prop.SetValue(obj, converted);
                    }
                    catch { }
                }
            }
            else if (node is ClassNode cls)
            {
                var prop = obj.GetType().GetProperty(cls.Name);
                if (prop != null)
                {
                    var nestedObj = Activator.CreateInstance(prop.PropertyType);
                    foreach (var child in cls.Children)
                    {
                        ApplyNodeToObject(child, nestedObj);
                    }
                    prop.SetValue(obj, nestedObj);


                    // update node children from nested object so UI sees changes
                    UpdateNodesFromObject(nestedObj, cls.Children);
                }
            }
        }

        public string Serialize(T config)
        {
            if (_nodes == null || _nodes.Count == 0)
            {
                // first init: generate nodes recursively from object
                _nodes = GenerateNodesFromObject(config);
            }
            else
            {
                UpdateNodesFromObject(config, _nodes);
            }
            return ConfigParser.Serialize(_nodes);
        }


        private List<ConfigNode> GenerateNodesFromObject(object obj)
        {
            var nodes = new List<ConfigNode>();
            foreach (var prop in obj.GetType().GetProperties())
            {
                var value = prop.GetValue(obj);
                if (value == null) continue;


                if (prop.PropertyType.IsClass && prop.PropertyType != typeof(string))
                {
                    nodes.Add(new ClassNode { Name = prop.Name, Children = GenerateNodesFromObject(value) });
                }
                else
                {
                    nodes.Add(new KeyValueNode { Key = prop.Name, Value = value.ToString() });
                }
            }
            return nodes;
        }


        private void UpdateNodesFromObject(object obj, List<ConfigNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node is KeyValueNode kv)
                {
                    var prop = obj.GetType().GetProperty(kv.Key);
                    if (prop != null)
                    {
                        var value = prop.GetValue(obj);
                        if (value != null) kv.Value = value.ToString();
                    }
                }
                else if (node is ClassNode cls)
                {
                    var prop = obj.GetType().GetProperty(cls.Name);
                    if (prop != null)
                    {
                        var nestedObj = prop.GetValue(obj);
                        if (nestedObj != null)
                            UpdateNodesFromObject(nestedObj, cls.Children);
                    }
                }
            }
        }
    }
    #endregion


}
