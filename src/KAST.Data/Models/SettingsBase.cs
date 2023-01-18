using System.Reflection;

namespace KAST.Data.Models
{
    public abstract class SettingsBase
    {
        // 1 name and properties cached in readonly fields
        private readonly string _name;
        private readonly PropertyInfo[] _properties;

        public SettingsBase()
        {
            var type = GetType();
            _name = type.Name;
            _properties = type.GetProperties();
        }

        public virtual void Load(KastDbContext dbContext)
        {
            // ARGUMENT CHECKING SKIPPED FOR BREVITY
            // Get settings for this type name
            var settings = dbContext.Settings.Where(w => w.Type == _name).ToList();

            foreach (var propertyInfo in _properties)
            {
                // get the setting from the settings list
                var setting = settings.SingleOrDefault(s => s.Name == propertyInfo.Name);
                if (setting != null)
                {
                    // Assign the setting values to the properties in the type inheriting this class
                    propertyInfo.SetValue(this, Convert.ChangeType(setting.Value, propertyInfo.PropertyType));
                }
            }
        }

        public virtual void Save(KastDbContext dbContext)
        {
            // Load existing settings for this type
            var settings = dbContext.Settings.Where(w => w.Type == _name).ToList();

            foreach (var propertyInfo in _properties)
            {
                object propertyValue = propertyInfo.GetValue(this, null);
                string value = (propertyValue == null) ? null : propertyValue.ToString();

                var setting = settings.SingleOrDefault(s => s.Name == propertyInfo.Name);
                if (setting != null)
                {
                    // Update existing value
                    setting.Value = value;
                }
                else
                {
                    // Create new setting
                    var newSetting = new Setting()
                    {
                        Name = propertyInfo.Name,
                        Type = _name,
                        Value = value,
                    };
                    dbContext.Settings.Add(newSetting);
                }
            }
        }
    }
}
