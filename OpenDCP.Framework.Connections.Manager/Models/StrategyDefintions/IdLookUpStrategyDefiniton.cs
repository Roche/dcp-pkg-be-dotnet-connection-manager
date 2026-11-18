using OpenDCP.Framework.Connections.Manager.Constants;
using OpenDCP.Framework.Connections.Manager.Helpers;
using OpenDCP.Framework.FormBuilder.Constants;
using OpenDCP.Framework.FormBuilder.Models;
using System.Text.Json.Nodes;

namespace OpenDCP.Framework.Connections.Manager.Models.StrategyDefintions
{
    public class IdLookUpStrategyDefiniton: StrategyDefintion
    {
        public IdLookUpStrategyDefiniton()
        {
            Type = ConnectionBridgeStrategyType.IdLookUp;
        }

        public Dictionary<string,string> IdMap { get; set; } = new Dictionary<string,string>();
        public override List<InputField> Form()
        {
            return new List<InputField>
            {
                new InputField
                {
                    Name = "map",
                    Label = "Id Mapping",
                    Description = "Define the dictonary of matching key in the source to the target connection as key-value pairs",
                    FieldType = (byte)InputFieldType.Array,
                    FieldSettings = new FieldSettings
                    {
                        DefaultValue = IdMap.Any() ? BuildIdMapDefault() : null,
                    },
                    Children = new List<InputField>
                    {
                        new InputField
                        {
                            Name = "id_scon",
                            Label = "Id source connection",
                            FieldType= (byte)InputFieldType.String,
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
                            Name = "id_tcon",
                            Label = "Id target connection",
                            FieldType= (byte)InputFieldType.String,
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
                .FirstOrDefault(v => v.Key == "map")?.Value;

            var map = JsonParseHelper.ToJsonArray(value) as JsonArray
                ?? throw new InvalidOperationException("The required field 'map' is not set or invalid.");

            foreach (var obj in map.OfType<JsonObject>())
            {
                if (!obj.TryGetPropertyValue("id_scon", out var sconNode) ||
                    !obj.TryGetPropertyValue("id_tcon", out var tconNode))
                {
                    throw new InvalidOperationException("Id mapping keys need a value.");
                }

                var idScon = sconNode!.GetValue<string>();
                var idTcon = tconNode!.GetValue<string>();

                if (!IdMap.TryAdd(idScon, idTcon))
                    throw new InvalidOperationException("Id mapping keys need to be unique.");
            }
        }

        private List<Dictionary<string, object>> BuildIdMapDefault()
        {
            return IdMap
                .Select(kvp => new Dictionary<string, object>
                {
                    ["FormArrayElementId"] = kvp.Key,
                    ["id_scon"] = kvp.Key,
                    ["id_tcon"] = kvp.Value
                })
                .ToList();
        }

    }
}
