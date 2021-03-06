using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.TemplateEngine.Abstractions.Mount;

namespace Microsoft.TemplateEngine.Abstractions
{
    public interface IGenerator : IIdentifiedComponent
    {
        Task<ICreationResult> CreateAsync(ITemplate template, IParameterSet parameters, IComponentManager componentManager, string targetDirectory);

        IParameterSet GetParametersForTemplate(ITemplate template);

        bool TryGetTemplateFromConfigInfo(IFileSystemInfo config, out ITemplate template, IFileSystemInfo localeConfig, IFile hostTemplateConfigFile);

        IList<ITemplate> GetTemplatesAndLangpacksFromDir(IMountPoint source, out IList<ILocalizationLocator> localizations);

        object ConvertParameterValueToType(ITemplateParameter parameter, string untypedValue, out bool valueResolutionError);
    }
}