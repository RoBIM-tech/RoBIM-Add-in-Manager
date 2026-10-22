using System.Xml;
using System.Xml.Linq;
namespace RoBIM.Manager.Core;

/// <summary>Validate compiled native Revit bundles without executing package code.</summary>
public static class NativeRevitPackages
{
    private static XDocument Read(string path)
    {
        using var reader=XmlReader.Create(path,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null});
        return XDocument.Load(reader);
    }
    public static void Validate(string source)
    {
        if (File.Exists(Path.Combine(source, RoBIM.Revit.Reload.NativeProtocol.ManifestName)))
        {
            ModuleReleases.ValidateNative(source);
            return; // command-only modules are loaded by the fixed Host, never directly registered.
        }
        var package=Read(SafeFiles.Child(source,"PackageContents.xml"));
        if(package.Root?.Name.LocalName!="ApplicationPackage") throw new InvalidDataException("Revit bundle 매니페스트 오류");
        var components=package.Root.Elements().Where(e=>e.Name.LocalName=="Components").ToArray();
        if(components.Length==0)throw new InvalidDataException("Revit Components 누락");
        foreach(var component in components)
        {
            var runtime=component.Elements().SingleOrDefault(e=>e.Name.LocalName=="RuntimeRequirements");
            if((string?)runtime?.Attribute("Platform")!="Revit" || (string?)runtime.Attribute("OS")!="Win64" ||
                !System.Text.RegularExpressions.Regex.IsMatch((string?)runtime.Attribute("SeriesMin")??"","^R20[0-9]{2}$") ||
                !System.Text.RegularExpressions.Regex.IsMatch((string?)runtime.Attribute("SeriesMax")??"","^R20[0-9]{2}$"))
                throw new InvalidDataException("Revit Win64 및 지원 버전 범위를 지정해야 합니다.");
            var entries=component.Elements().Where(e=>e.Name.LocalName=="ComponentEntry").ToArray();
            if(entries.Length==0)throw new InvalidDataException("Revit ComponentEntry 누락");
            foreach(var entry in entries)
            {
                var relative=((string?)entry.Attribute("ModuleName")??"").Replace('\\','/');
                if(relative.StartsWith("./"))relative=relative[2..];
                if(!relative.EndsWith(".addin",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Revit ModuleName은 .addin이어야 합니다.");
                var manifest=SafeFiles.Child(source,relative);
                var addins=Read(manifest);
                if(addins.Root?.Name.LocalName!="RevitAddIns")throw new InvalidDataException("RevitAddIns 루트 오류");
                var registrations=addins.Root.Elements("AddIn").ToArray();
                if(registrations.Length==0)throw new InvalidDataException("Revit AddIn 등록 누락");
                foreach(var addin in registrations)
                {
                    if((string?)addin.Attribute("Type") is not("Application" or "Command") ||
                        !Guid.TryParse((string?)addin.Element("AddInId"),out _) || string.IsNullOrWhiteSpace((string?)addin.Element("FullClassName")))
                        throw new InvalidDataException("Revit AddIn Type / ID / 진입점 오류");
                    var assembly=((string?)addin.Element("Assembly")??"").Replace('\\','/');
                    if(assembly.StartsWith("./"))assembly=assembly[2..];
                    if(!assembly.EndsWith(".dll",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Revit Assembly는 상대 DLL 경로여야 합니다.");
                    if(!File.Exists(SafeFiles.Child(Path.GetDirectoryName(manifest)!,assembly)))throw new InvalidDataException("Revit 실행 DLL 누락");
                }
            }
        }
    }
}
