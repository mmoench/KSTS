using System.Collections.Generic;
using System.Linq;
using UnityEngine;


namespace KSTS
{
    class GUIFlightsTab
    {
        private static Vector2 scrollPos = Vector2.zero;

        public static void Display()
        {
            scrollPos = GUILayout.BeginScrollView(scrollPos, GUI.scrollStyle);
            if (MissionController.missions.Count == 0)
            {
                GUILayout.Label("<b>No active missions.</b>");
            }
            else
            {
                List<GUIContent> contents = new List<GUIContent>();
                MissionController.missions.Sort((x, y) => x.eta.CompareTo(y.eta)); // Sort list by ETA
                foreach (Mission mission in MissionController.missions)
                {
                    string missionVesselName = "";
                    if (mission.GetProfile() != null) missionVesselName = mission.GetProfile().vesselName;
                    contents.Add(new GUIContent(mission.GetDescription(), GUI.GetVesselThumbnail(missionVesselName)));
                }
                
                GUILayout.SelectionGrid(-1, contents.ToArray(), 1, GUI.selectionGridStyle);
            }
            GUILayout.EndScrollView();
        }
    }
}
