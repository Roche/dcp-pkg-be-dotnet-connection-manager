using OpenDCP.Framework.Configuration.Model.Extensions;
using OpenDCP.Framework.Connections.Manager.Constants;
using OpenDCP.Framework.Connections.Manager.Helpers;
using OpenDCP.Framework.FormBuilder.Constants;
using OpenDCP.Framework.FormBuilder.Models;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace OpenDCP.Framework.Connections.Manager.Models.StrategyDefintions
{
    public class AttributeMatchingStrategyDefiniton : StrategyDefintion
    {
        public AttributeMatchingStrategyDefiniton()
        {
            Type = ConnectionBridgeStrategyType.Attribute;
        }

        [JsonInclude]
        public List<(string sKey, string tKey)> Keys = new List<(string sKey, string tKey)>();

        public override List<InputField> Form()
        {
            return new List<InputField>
            {
                new InputField
                {
                    Name = "mapping_keys",
                    Label = "Mapping Keys",
                    Description = "Define the dictonary of matching key in the source to the target connection as key-value pairs",
                    FieldType = (byte)InputFieldType.Array,
                    FieldValidations = new List<FieldValidationDTO>
                    {
                        new FieldValidationDTO
                        {
                            Type = InputFieldValidationType.Min.ToName(),
                            Value = "1"
                        }
                    },
                    FieldSettings = new FieldSettings
                    {
                        DefaultValue = Keys.Any() ? BuildMappingKeysDefault() : null,
                    },
                    Children = new List<InputField>
                    {
                        new InputField
                        {
                            Name = "key_scon",
                            Label = "Key source connection",
                            FieldType = (byte)InputFieldType.String,
                            FieldValidations = new List<FieldValidationDTO>
                            {
                                new FieldValidationDTO
                                {
                                    Type = InputFieldValidationType.Required.ToString(),
                                }
                            }
                        },
                        new InputField
                        {
                            Name = "key_tcon",
                            Label = "Key target connection",
                            FieldType = (byte)InputFieldType.String,
                            FieldValidations = new List<FieldValidationDTO>
                            {
                                new FieldValidationDTO
                                {
                                    Type = InputFieldValidationType.Required.ToString(),
                                }
                            }
                        }
                    }
                }
            };
        }

        public override void LoadFromForm(List<FormValue> values)
        {
            var value = values
                .FirstOrDefault(v => v.Key == "mapping_keys")?.Value;

            var map = JsonParseHelper.ToJsonArray(value) as JsonArray 
                ?? throw new InvalidOperationException("The required field 'mapping_keys' is not set or invalid.");

            foreach (var obj in map.OfType<JsonObject>())
            {
                if (!obj.TryGetPropertyValue("key_scon", out var sconNode) ||
                    !obj.TryGetPropertyValue("key_tcon", out var tconNode))
                {
                    throw new InvalidOperationException("Id mapping keys need a value.");
                }

                var keyScon = sconNode!.GetValue<string>();
                var keyTcon = tconNode!.GetValue<string>();

                Keys.Add(new(keyScon, keyTcon));
            }
        }

        private List<Dictionary<string, object>> BuildMappingKeysDefault()
        {
            return Keys
                .Select(k => new Dictionary<string, object>
                {
                    ["FormArrayElementId"] = Math.Abs($"{k.sKey}::{k.tKey}".GetHashCode()).ToString(),
                    ["key_scon"] = k.sKey,
                    ["key_tcon"] = k.tKey
                })
                .ToList();
        }
    }
}
