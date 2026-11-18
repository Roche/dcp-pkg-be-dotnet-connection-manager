using OpenDCP.Framework.Connections.Manager.Constants;
using OpenDCP.Framework.FormBuilder.Constants;
using OpenDCP.Framework.FormBuilder.Models;

namespace OpenDCP.Framework.Connections.Manager.Models.StrategyDefintions
{
    public class QueryLookUpStrategyDefiniton : StrategyDefintion
    {
        public QueryLookUpStrategyDefiniton()
        {
            Type = ConnectionBridgeStrategyType.QueryLookUp;
        }

        public string Query { get; set; } = string.Empty;

        public override List<InputField> Form()
        {
            return new List<InputField>
            {
                new InputField
                {
                    Name = "query",
                    Label = "Query",
                    Description = "Query template. Use {id} as a placeholder for the identifier value.",
                    FieldType = (byte)InputFieldType.String,
                    FieldSettings = new FieldSettings { DefaultValue = Query },
                    FieldValidations = new List<FieldValidationDTO>
                    {
                        new FieldValidationDTO { Type = InputFieldValidationType.Required.ToString() }
                    }
                }
            };
        }

        public override void LoadFromForm(List<FormValue> values)
        {
            Query = values.FirstOrDefault(v => v.Key == "query")?.Value?.ToString()
                ?? throw new InvalidOperationException("The required field 'query' is not set or invalid.");
        }
    }
}
