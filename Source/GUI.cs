using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using KSP.UI.Screens; // For "ApplicationLauncherButton"
using System.Text.RegularExpressions;

namespace KSTS
{
    // Helper-Class to draw the window in the flight scene:
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class GUIFlight : UnityEngine.MonoBehaviour
    {
        public void OnGUI()
        {
            if (GUI.showGui)
            {
                GUI.windowPosition = GUILayout.Window(100, GUI.windowPosition, OnWindow, "", GUI.windowStyle);
            }
        }

        private void OnWindow(int windowId)
        {
            try
            {
                GUI.DrawWindow();
            }
            catch (Exception e)
            {
                Debug.LogError("[KSTS] KSTSGUIFlight.OnWindow(): " + e.ToString());
            }
        }
    }

    // Helper-Class to draw the window in the tracking-station scene:
    [KSPAddon(KSPAddon.Startup.TrackingStation, false)]
    public class GUITrackingStation: UnityEngine.MonoBehaviour
    {
        public void OnGUI()
        {
            if (GUI.showGui)
            {
                GUI.windowPosition = GUILayout.Window(100, GUI.windowPosition, OnWindow, "", GUI.windowStyle);
            }
        }

        private void OnWindow(int windowId)
        {
            try
            {
                GUI.DrawWindow();
            }
            catch (Exception e)
            {
                Debug.LogError("[KSTS] KSTSGUITrackingStation.OnWindow(): " + e.ToString());
            }
        }
    }

    // Helper-Class to draw the window in the space-center scene:
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class GUISpaceCenter : UnityEngine.MonoBehaviour
    {
        public void OnGUI()
        {
            if (GUI.showGui)
            {
                GUI.windowPosition = GUILayout.Window(100, GUI.windowPosition, OnWindow, "", GUI.windowStyle);
            }
        }

        private void OnWindow(int windowId)
        {
            try
            {
                GUI.DrawWindow();
            }
            catch (Exception e)
            {
                Debug.LogError("[KSTS] KSTSGUISpaceCenter.OnWindow(): " + e.ToString());
            }
        }
    }

    class GUIRichValueSelector
    {
        private string name = "";
        private double value;
        public double Value { get { return this.value; } }
        private double minValue = 0;
        private double maxValue = 0;
        private string unit = "";
        private double lastValue = 0;
        private string textValue = "";
        private string valueFormat = "";
        private bool showMinMax = true;

        private GUIStyle validFieldStyle;
        private GUIStyle invalidFieldStyle;

        public GUIRichValueSelector(string name, double value, string unit, double minValue, double maxValue, bool showMinMax, string valueFormat)
        {
            this.name = name;
            this.value = value;
            this.lastValue = value;
            this.unit = unit;
            this.minValue = minValue;
            this.maxValue = maxValue;
            this.showMinMax = showMinMax;
            this.valueFormat = valueFormat;
            this.textValue = this.value.ToString(this.valueFormat) + this.unit;

            this.validFieldStyle = new GUIStyle(GUI.textFieldStyle) { alignment = TextAnchor.MiddleRight, stretchWidth = false, fixedWidth = 320 };
            this.invalidFieldStyle = new GUIStyle(this.validFieldStyle);
            this.invalidFieldStyle.normal.textColor = Color.red;
            this.invalidFieldStyle.focused.textColor = Color.red;
        }

        protected bool TryParseTextValue()
        {
            double parsedValue;
            string text = this.textValue.Replace(",", "");
            text = Regex.Replace(text, this.unit + "$", "").Trim();
            if (!Double.TryParse(text, out parsedValue)) return false;
            if (parsedValue > this.maxValue) return false;
            if (parsedValue < this.minValue) return false;
            this.value = parsedValue;
            return true;
        }

        public double Display()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(this.name + ":", new GUIStyle(GUI.labelStyle) { stretchWidth = true });
            this.textValue = GUILayout.TextField(this.textValue, this.TryParseTextValue() ? this.validFieldStyle : this.invalidFieldStyle);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (this.showMinMax) GUILayout.Label(this.minValue.ToString(valueFormat) + this.unit, new GUIStyle(GUI.labelStyle) { stretchWidth = false, alignment = TextAnchor.MiddleLeft });
            this.value = GUILayout.HorizontalSlider((float)this.value, (float)this.minValue, (float)this.maxValue);
            if (this.showMinMax) GUILayout.Label(this.maxValue.ToString(valueFormat) + this.unit, new GUIStyle(GUI.labelStyle) { stretchWidth = false, alignment = TextAnchor.MiddleRight });
            GUILayout.EndHorizontal();

            if (this.value != this.lastValue)
            {
                this.textValue = this.value.ToString(this.valueFormat) + this.unit;
                this.lastValue = this.value;
            }

            return this.value;
        }
    }

    // Helper class to store a ships template (from the craft's save-file) together with its generated thumbnail:
    public class CachedShipTemplate
    {
        public ShipTemplate template;
        public Texture2D thumbnail;
    }

    // Creates the button and contains the functionality to draw the GUI-window (we want to use the same window
    // for different scenes, that is why we have a few helper-classes above):
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class GUI : UnityEngine.MonoBehaviour
    {
        public static Rect windowPosition = new Rect(300, 60, 450, 400);
        public static GUIStyle windowStyle = new GUIStyle(HighLogic.Skin.window) { fixedWidth = 450f, fixedHeight = 500f };
        public static bool showGui = false;

        // Styles:
        public static GUIStyle labelStyle = new GUIStyle("Label");
        public static GUIStyle buttonStyle = new GUIStyle("Button");
        public static GUIStyle textFieldStyle = new GUIStyle("TextField");
        public static GUIStyle scrollStyle = HighLogic.Skin.scrollView;
        public static GUIStyle selectionGridStyle = new GUIStyle(GUI.buttonStyle) { richText = true, fontStyle = FontStyle.Normal, alignment = TextAnchor.UpperLeft };

        // Common resources:
        private static ApplicationLauncherButton button = null;
        private static Texture2D buttonIcon = null;
        private static int selectedMainTab = 0;
        public static Texture2D placeholderImage = null;
        public static List<CachedShipTemplate> shipTemplates = null;

        private static string helpText = "";
        private static Vector2 helpTabScrollPos = Vector2.zero;

        void Awake()
        {
            if (buttonIcon == null)
            {
                buttonIcon = new Texture2D(36, 36, TextureFormat.RGBA32, false);
                buttonIcon.LoadImage(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "KSTS_icon.png")));
            }
            if (placeholderImage == null)
            {
                placeholderImage = new Texture2D(275, 275, TextureFormat.RGBA32, false);
                placeholderImage.LoadImage(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "placeholder.png")));
                placeholderImage = GUI.ResizeTexture(placeholderImage, 64, 64); // Default-size for our ship-icons
            }

            if (helpText == "")
            {
                string helpFilename = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "/help.txt";
                if (File.Exists(helpFilename)) helpText = File.ReadAllText(helpFilename);
                else helpText = "Help-file not found.";
            }

            // Add event-handlers to create and destroy our button:
            GameEvents.onGUIApplicationLauncherReady.Remove(ReadyEvent);
            GameEvents.onGUIApplicationLauncherReady.Add(ReadyEvent);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(DestroyEvent);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(DestroyEvent);
        }

        // Fires when a scene is ready so we can install our button.
        public void ReadyEvent()
        {
            if (ApplicationLauncher.Ready && button == null)
            {
                var visibleScense = ApplicationLauncher.AppScenes.SPACECENTER | ApplicationLauncher.AppScenes.TRACKSTATION | ApplicationLauncher.AppScenes.FLIGHT;
                button = ApplicationLauncher.Instance.AddModApplication(GuiOn, GuiOff, null, null, null, null, visibleScense, buttonIcon);
            }
        }

        // Fires when a scene is unloaded and we should destroy our button:
        public void DestroyEvent()
        {
            if (button == null) return;
            ApplicationLauncher.Instance.RemoveModApplication(button);
            button = null;
            showGui = false;
        }

        private void GuiOn()
        {
            showGui = true;
            GUI.UpdateShipTemplateCache();
        }

        private void GuiOff()
        {
            showGui = false;
        }

        public static string FormatDuration(double duration)
        {
            double seconds = duration % 60;
            int minutes = ((int)(duration / 60)) % 60;
            int hours = ((int)(duration / 60 / 60)) % 24;
            int days = ((int)(duration / 60 / 60 / 24));
            return String.Format("{0:0}/{1:00}:{2:00}:{3:00.00}", days, hours, minutes, seconds);
        }

        // Returns a thumbnail for a given vessel-name (used to find fitting images for vessels used in mission-profiles):
        public static Texture2D GetVesselThumbnail(string vesselName)
        {
            foreach (CachedShipTemplate cachedTemplate in GUI.shipTemplates)
            {
                // This is strictly not correct, because the player could name VAB and SPH vessels the same, but this is easier
                // than to also save the editor-type in the mission-profile:
                if (cachedTemplate.template.shipName.ToString() == vesselName) return cachedTemplate.thumbnail;
            }
            return GUI.placeholderImage; // Fallback
        }

        // For some reason Unity has no resize method, so we have to implement our own:
        public static Texture2D ResizeTexture(Texture2D input, int width, int height)
        {
            Texture2D small = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            float rx = (float)input.width / (float)small.width;
            float ry = (float)input.height / (float)small.height;
            for (int y = 0; y < small.height; y++)
            {
                int sy = (int)Math.Round(ry * y);
                for (int x = 0; x < small.width; x++)
                {
                    int sx = (int)Math.Round(rx * x);
                    small.SetPixel(x, y, input.GetPixel(sx, sy));
                }
            }
            small.Apply();
            return small;
        }

        // Updates the cache we use to store the meta-data of the various ships the player has designed:
        public static void UpdateShipTemplateCache()
        {
            if (GUI.shipTemplates == null) GUI.shipTemplates = new List<CachedShipTemplate>();
            string[] editorFacilities = { "VAB", "SPH" }; // This is usually an enum, but we need the string later.
            GUI.shipTemplates.Clear();

            foreach (string editorFacility in editorFacilities)
            {
                string shipDirectory = KSPUtil.ApplicationRootPath + "/saves/" + HighLogic.SaveFolder + "/Ships/" + editorFacility; // Directory where the crafts are stored for the current game.
                if (!Directory.Exists(shipDirectory)) continue;

                // Get all crafts the player has designed in this savegame:
                foreach (string craftFile in Directory.GetFiles(shipDirectory, "*.craft"))
                {
                    try
                    {
                        if (Path.GetFileNameWithoutExtension(craftFile) == "Auto-Saved Ship") continue; // Skip these, they would lead to duplicates, we only use finished crafts.
                        CachedShipTemplate cachedTemplate = new CachedShipTemplate();

                        cachedTemplate.template = ShipConstruction.LoadTemplate(craftFile);
                        if (cachedTemplate.template == null) continue;
                        if (cachedTemplate.template.shipPartsExperimental || !cachedTemplate.template.shipPartsUnlocked) continue; // We won't bother with ships we can't use anyways.

                        // Try to load the thumbnail for this craft:
                        string thumbFile = KSPUtil.ApplicationRootPath + "/thumbs/" + HighLogic.SaveFolder + "_" + editorFacility + "_" + cachedTemplate.template.shipName + ".png";
                        Texture2D thumbnail;
                        if (File.Exists(thumbFile))
                        {
                            thumbnail = new Texture2D(256, 256, TextureFormat.RGBA32, false);
                            thumbnail.LoadImage(File.ReadAllBytes(thumbFile));
                        }
                        else thumbnail = placeholderImage;

                        // The thumbnails are rather large, so we have to resize them first:
                        cachedTemplate.thumbnail = GUI.ResizeTexture(thumbnail, 64, 64);

                        GUI.shipTemplates.Add(cachedTemplate);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("[KSTS] UpdateShipTemplateCache() processing '"+ craftFile + "': " + e.ToString());
                    }
                }
            }

            GUI.shipTemplates.Sort((x, y) => x.template.shipName.CompareTo(y.template.shipName));
        }

        // Resets all internally used objects and caches, can be used for example when a savegame is loaded:
        public static void Reset()
        {
            UpdateShipTemplateCache();
            GUIStartDeployMissionTab.Reset();
            GUIStartTransportMissionTab.Reset();
            GUIRecordingTab.Reset();
        }

        // Is called by our helper-classes to draw the actual window:
        public static void DrawWindow()
        {
            if (!showGui) return;
            try
            {
                GUILayout.BeginVertical();

                // Title:
                GUILayout.BeginArea(new Rect(0, 3, windowStyle.fixedWidth, 20));
                GUILayout.Label("<size=14><b>Kerbal Space Transport System</b></size>", new GUIStyle(GUI.labelStyle) { fixedWidth = windowStyle.fixedWidth, alignment = TextAnchor.MiddleCenter });
                GUILayout.EndArea();

                // Tab-Switcher:
                string[] toolbarStrings = new string[] {"Flights", "Deploy", "Transport", "Construct", "Record", "Help" };
                selectedMainTab = GUILayout.Toolbar(selectedMainTab, toolbarStrings);

                switch (selectedMainTab)
                {
                    // Flights:
                    case 0:
                        GUIFlightsTab.Display();
                        break;

                    // Deploy:
                    case 1:
                        if (GUIStartDeployMissionTab.Display()) selectedMainTab = 0;
                        break;

                    // Transport:
                    case 2:
                        if (GUIStartTransportMissionTab.Display()) selectedMainTab = 0;
                        break;

                    // Construct:
                    case 3:
                        // TODO: Add Construct Missions
                        GUILayout.Label("<b>Not implemented, yet.</b>");
                        break;

                    // Record:
                    case 4:
                        GUIRecordingTab.Display();
                        break;

                    // Help:
                    case 5:
                        helpTabScrollPos = GUILayout.BeginScrollView(helpTabScrollPos, GUI.scrollStyle);
                        GUILayout.Label(helpText);
                        GUILayout.EndScrollView();
                        // TODO: Write some Help-Texts
                        break;

                    default:
                        GUILayout.Label("<b>Not implemented yet.</b>");
                        break;
                }

                GUILayout.EndVertical();
                UnityEngine.GUI.DragWindow();
            }
            catch (Exception e)
            {
                Debug.LogError("[KSTS] DrawWindow(): " + e.ToString());
            }
        }
    }
}
