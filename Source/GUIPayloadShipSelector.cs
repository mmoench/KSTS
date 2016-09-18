using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace KSTS
{
    class GUIPayloadShipSelector
    {
        private Vector2 scrollPos = Vector2.zero;
        private int selectedIndex = -1;
        public CachedShipTemplate payload = null;

        // Makes sure that the cached settings are still valid (eg if the player has deleted the selected payload):
        private void CheckInternals()
        {
            if (!GUI.shipTemplates.Contains(payload) || selectedIndex < 0 || selectedIndex >= GUI.shipTemplates.Count)
            {
                selectedIndex = -1;
                payload = null;
            }
        }

        // Displays the currently selected ship-payload and returns true, if the player has deselected it:
        public bool DisplaySelected()
        {
            CheckInternals();
            if (payload == null) return true;
            GUILayout.BeginHorizontal();
            GUILayout.Label("<size=14><b>Payload:</b></size>", new GUIStyle(GUI.labelStyle) { stretchWidth = true });
            if (GUILayout.Button("<size=14><color=#F9FA86><b>" + payload.template.shipName + "</b></color> (Mass: " + payload.template.totalMass.ToString("0.0t") + ")</size>", new GUIStyle(GUI.buttonStyle) { alignment = TextAnchor.MiddleRight, stretchWidth = false, fixedWidth = 320 }))
            {
                selectedIndex = -1;
                payload = null;
            }
            GUILayout.EndHorizontal();
            return payload == null;
        }

        // Shows a list of all available ship-payloads and returns true, if the player has selected one:
        public bool DisplayList()
        {
            CheckInternals();
            GUILayout.Label("<size=14><b>Payload:</b></size>");
            scrollPos = GUILayout.BeginScrollView(scrollPos, GUI.scrollStyle);

            // Show list with all possible payloads:
            List<GUIContent> contents = new List<GUIContent>();
            foreach (CachedShipTemplate ship in GUI.shipTemplates)
            {
                contents.Add(new GUIContent(
                    "<color=#F9FA86><b>" + ship.template.shipName + "</b></color>\n" +
                    "<color=#FFFFFF><b>Size:</b> " + ship.template.shipSize.x.ToString("0.0m") + ", " + ship.template.shipSize.y.ToString("0.0m") + ", " + ship.template.shipSize.z.ToString("0.0m") + "</color>\n" +
                    "<color=#FFFFFF><b>Mass:</b> " + ship.template.totalMass.ToString("0.0t") + "</color>\n" +
                    "<color=#B3D355><b>Cost:</b> " + ship.template.totalCost.ToString("#,##0√") + "</color>"
                    , ship.thumbnail
                ));
            }
            if ((selectedIndex = GUILayout.SelectionGrid(selectedIndex, contents.ToArray(), 1, GUI.selectionGridStyle)) >= 0)
            {
                // The player has selected a payload:
                payload = GUI.shipTemplates[selectedIndex];
            }
            GUILayout.EndScrollView();
            return payload != null;
        }
    }
}
