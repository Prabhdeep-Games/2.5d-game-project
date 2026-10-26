using System.IO;
using UnityEditor;
using UnityEngine;

namespace TheSpaceBetween.EditorTools
{
    /// <summary>
    /// Adds script creation entries to the Project window's Create menu:
    /// right click in the Project window, then Create, then The Space Between, then Scripts.
    ///
    /// Each entry creates a new C# file from a template in the Templates folder and uses Unity's
    /// standard create-and-rename flow, exactly like the built-in "C# Script" entry. Asset entries
    /// (Health Data, Game Event, and so on) come from CreateAssetMenu attributes on the asset
    /// classes and appear in the same The Space Between submenu.
    ///
    /// To add a new template: put a "Name.cs.txt" file in the Templates folder (use #SCRIPTNAME#
    /// for the class name) and add one entry below.
    /// </summary>
    public static class ScriptTemplateMenu
    {
        private const string MenuPath = "Assets/Create/The Space Between/Scripts/";
        private const int MenuPriority = 70;
        private const string TemplateFolderName = "Templates";
        private const string TemplateSuffix = ".cs.txt";

        //==================== MENU ENTRIES =====================
        [MenuItem(MenuPath + "Component", false, MenuPriority)]
        private static void CreateComponent()
        {
            CreateScript("Component", "NewComponent");
        }

        [MenuItem(MenuPath + "Data (ScriptableObject)", false, MenuPriority + 1)]
        private static void CreateData()
        {
            CreateScript("ScriptableObjectData", "NewData");
        }

        [MenuItem(MenuPath + "State", false, MenuPriority + 2)]
        private static void CreateState()
        {
            CreateScript("State", "NewState");
        }

        [MenuItem(MenuPath + "Sensor", false, MenuPriority + 3)]
        private static void CreateSensor()
        {
            CreateScript("Sensor", "NewSensor");
        }

        [MenuItem(MenuPath + "Interface", false, MenuPriority + 4)]
        private static void CreateInterface()
        {
            CreateScript("Interface", "INewInterface");
        }

        //==================== PRIVATE =====================
        private static void CreateScript(string templateName, string defaultName)
        {
            string templatePath = FindTemplate(templateName);
            if (templatePath == null)
            {
                Debug.LogError($"Script template '{templateName}{TemplateSuffix}' was not found. " +
                               $"It must be inside a folder named '{TemplateFolderName}' in this project.");
                return;
            }

            ProjectWindowUtil.CreateScriptAssetFromTemplateFile(templatePath, defaultName + ".cs");
        }

        // The template is searched by name instead of a fixed path, so the Templates folder can be
        // moved inside the project without breaking the menu.
        private static string FindTemplate(string templateName)
        {
            string fileName = templateName + TemplateSuffix;
            string[] guids = AssetDatabase.FindAssets(templateName + ".cs t:TextAsset");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                bool nameMatches = Path.GetFileName(path) == fileName;
                bool inTemplateFolder = path.Contains("/" + TemplateFolderName + "/");

                if (nameMatches && inTemplateFolder)
                {
                    return path;
                }
            }

            return null;
        }
    }
}
