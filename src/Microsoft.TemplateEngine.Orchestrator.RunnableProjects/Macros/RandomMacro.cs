using System;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Core.Contracts;
using Microsoft.TemplateEngine.Orchestrator.RunnableProjects.Macros.Config;
using Newtonsoft.Json.Linq;

namespace Microsoft.TemplateEngine.Orchestrator.RunnableProjects.Macros
{
    public class RandomMacro : IMacro, IDeferredMacro
    {
        public Guid Id => new Guid("011E8DC1-8544-4360-9B40-65FD916049B7");

        public string Type => "random";

        public void EvaluateConfig(IVariableCollection vars, IMacroConfig rawConfig, IParameterSet parameters, ParameterSetter setter)
        {
            RandomMacroConfig config = rawConfig as RandomMacroConfig;

            if (config == null)
            {
                throw new InvalidCastException("Couldn't cast the rawConfig as RandomMacroConfig");
            }

            Random rnd = new Random();
            int value = rnd.Next(config.Low, config.High);

            Parameter p = new Parameter
            {
                IsVariable = true,
                Name = config.VariableName
            };

            setter(p, value.ToString());
        }

        public void EvaluateDeferredConfig(IVariableCollection vars, IMacroConfig rawConfig, IParameterSet parameters, ParameterSetter setter)
        {
            GeneratedSymbolDeferredMacroConfig deferredConfig = rawConfig as GeneratedSymbolDeferredMacroConfig;

            if (deferredConfig == null)
            {
                throw new InvalidCastException("Couldn't cast the rawConfig as a GeneratedSymbolDeferredMacroConfig");
            }

            int low;
            int high;

            if (!deferredConfig.Parameters.TryGetValue("low", out JToken lowToken))
            {
                throw new ArgumentNullException("low");
            }
            else
            {
                low = lowToken.Value<int>();
            }

            if (!deferredConfig.Parameters.TryGetValue("high", out JToken highToken))
            {
                high = int.MaxValue;
            }
            else
            {
                high = highToken.Value<int>();
            }

            IMacroConfig realConfig = new RandomMacroConfig(deferredConfig.VariableName, low, high);
            EvaluateConfig(vars, realConfig, parameters, setter);
        }
    }
}