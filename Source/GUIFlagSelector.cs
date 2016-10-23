using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using UnityEngine;

namespace KSTS
{
    class GUIFlagSelector
    {
        private FlagBrowser flagBrowser = null;

        public Texture2D flagIcon = null;   // Icon which can be used to preview the selected flag
        public string flagFilename;         // Actual filename of the physical flag-file
        public string flagURL;              // Internal reference KSP uses to assign flags

        public GUIFlagSelector()
        {
            SetFlagSelectedFlag(HighLogic.CurrentGame.flagURL);
        }

        private void SetFlagSelectedFlag(string flagURL)
        {
            this.flagURL = flagURL;
            flagFilename = Path.Combine(Path.Combine(KSPUtil.ApplicationRootPath, "GameData"), flagURL);

            // Try to find an existing image we can load as preview texture:
            if (File.Exists(flagFilename + ".png")) flagFilename += ".png";
            else if (File.Exists(flagFilename + ".truecolor")) flagFilename += ".truecolor";
            else if (File.Exists(flagFilename + "_scaled.truecolor")) flagFilename += "_scaled.truecolor";

            if (File.Exists(flagFilename))
            {
                flagIcon = new Texture2D(58, 32, TextureFormat.RGBA32, false);
                flagIcon.LoadImage(File.ReadAllBytes(flagFilename));
                flagIcon = GUI.ResizeTexture(flagIcon, 96, 60);
            }
            else
            {
                flagIcon = null;
            }
        }

        private void OnFlagCancelled()
        {
            flagBrowser = null;
        }

        private void OnFlagSelected(FlagBrowser.FlagEntry selected)
        {
            if (selected?.textureInfo?.name != null) {
                SetFlagSelectedFlag(selected.textureInfo.name);
            }
            flagBrowser = null;
        }

        private void ShowFlagBrowser()
        {
            if (flagBrowser == null)
            {
                flagBrowser = (UnityEngine.Object.Instantiate((UnityEngine.Object)(new FlagBrowserGUIButton(null, null, null, null)).FlagBrowserPrefab) as GameObject).GetComponent<FlagBrowser>();
                flagBrowser.OnFlagSelected = this.OnFlagSelected;
                flagBrowser.OnDismiss = this.OnFlagCancelled;
            }
        }

        public void ShowButton()
        {
            GUILayout.Label("<size=14><b>Mission-Flag:</b></size>", GUI.labelStyle);

            bool pressed = false;
            if (flagIcon != null)
            {
                pressed = GUILayout.Button(flagIcon, new GUIStyle(GUI.buttonStyle) { stretchWidth = false });
            }
            else
            {
                pressed = GUILayout.Button("N/A", new GUIStyle(GUI.buttonStyle) { stretchWidth = false });
            }

            if (pressed) ShowFlagBrowser();
        }
    }
}
