namespace KAST.Data.Attributes
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class EnvVariableAttribute : Attribute
    {
        public string Key { get; }
        public string? DefaultValue { get; }

        public EnvVariableAttribute(string key)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
        }

        public EnvVariableAttribute(string key, string defaultValue) : this(key)
        {
            DefaultValue = defaultValue;
        }

        public bool IsOverridden() => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Key));
    }
}
