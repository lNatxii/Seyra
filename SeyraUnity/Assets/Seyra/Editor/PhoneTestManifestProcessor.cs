using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor.Android;

namespace Seyra.Editor
{
    // Only the generated phone Gradle project is changed; Quest settings and manifests remain intact.
    public sealed class PhoneTestManifestProcessor : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => int.MaxValue;
        public void OnPostGenerateGradleAndroidProject(string path)
        {
            if (!SeyraAndroidBuild.IsPhoneTestBuild) return;
            string manifestPath = Path.Combine(path, "src/main/AndroidManifest.xml");
            var document = XDocument.Load(manifestPath);
            XNamespace android = "http://schemas.android.com/apk/res/android";
            foreach (var element in document.Descendants().ToArray())
            {
                string name = (string)element.Attribute(android + "name") ?? "";
                if (name.StartsWith("com.oculus.", System.StringComparison.Ordinal)
                    || name.StartsWith("oculus.", System.StringComparison.Ordinal)
                    || name == "android.hardware.vr.headtracking"
                    || element.Name.NamespaceName == "http://schemas.horizonos/sdk")
                    element.Remove();
            }
            document.Save(manifestPath);
        }
    }
}
