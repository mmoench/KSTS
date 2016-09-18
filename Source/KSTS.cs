using System;
using System.Linq;
using UnityEngine;
using System.Reflection;
using System.Collections.Generic;

namespace KSTS
{
    public class Saveable
    {
        // Returns a config-node which contains all public attributes of this object to be saved:
        public ConfigNode CreateConfigNode(string name)
        {
            ConfigNode node = new ConfigNode(name);
            FieldInfo[] fields = this.GetType().GetFields();
            foreach (FieldInfo field in fields)
            {
                if (!field.IsPublic) continue; // Only public attributes should contain persistent values worth saving.
                if (field.IsLiteral) continue; // Don't save constants.
                if (field.GetValue(this) == null) continue;

                // Save all elements of the list by creating multiple config-nodes with the same name:
                if (field.FieldType == typeof(List<string>))
                {
                    List<string> list = (List<string>)field.GetValue(this);
                    if (list != null) foreach (string element in list)
                    {
                        node.AddValue(field.Name.ToString(), element);
                    }
                }
                // Save dictionary-values in a sub-node:
                else if (field.FieldType == typeof(Dictionary<string, double>))
                {
                    ConfigNode dictNode = node.AddNode(field.Name.ToString());
                    foreach (KeyValuePair<string, double> item in (Dictionary<string, double>)field.GetValue(this))
                    {
                        dictNode.AddValue(item.Key, item.Value.ToString());
                    }
                }
                // Use orbit helper-class to save compley orbit-object:
                else if (field.FieldType == typeof(Orbit))
                {
                    node.AddNode(GUIOrbitEditor.SaveOrbitToNode((Orbit)field.GetValue(this)));
                }
                // Default; save as string:
                else node.AddValue(field.Name.ToString(), field.GetValue(this));
            }
            return node;
        }

        // Creates a new object from the given config-node to fill all of the objects public attributes:
        public static object CreateFromConfigNode(ConfigNode node, object obj)
        {
            FieldInfo[] fields = obj.GetType().GetFields();
            foreach (FieldInfo field in fields)
            {
                if (!field.IsPublic) continue; // Only public attributes should contain persistent values worth saving.
                if (field.IsLiteral) continue; // Don't load constants.
                if (!node.HasValue(field.Name.ToString()) && !node.HasNode(field.Name.ToString())) continue; // Should only happen when the savegame is from a different version.
                if (field.FieldType == typeof(double)) field.SetValue(obj, double.Parse(node.GetValue(field.Name.ToString())));
                else if (field.FieldType == typeof(MissionType)) field.SetValue(obj, Enum.Parse(typeof(MissionType), node.GetValue(field.Name.ToString())));
                else if (field.FieldType == typeof(MissionProfileType)) field.SetValue(obj, Enum.Parse(typeof(MissionProfileType), node.GetValue(field.Name.ToString())));
                else if (field.FieldType == typeof(FlightRecordingStatus)) field.SetValue(obj, Enum.Parse(typeof(FlightRecordingStatus), node.GetValue(field.Name.ToString())));
                else if (field.FieldType == typeof(int)) field.SetValue(obj, int.Parse(node.GetValue(field.Name.ToString())));
                else if (field.FieldType == typeof(bool)) field.SetValue(obj, bool.Parse(node.GetValue(field.Name.ToString())));
                else if (field.FieldType == typeof(Guid) || field.FieldType == typeof(Guid?)) field.SetValue(obj, new Guid(node.GetValue(field.Name.ToString())));
                // Restore String-Lists by loading all nodes with the name of this field:
                else if (field.FieldType == typeof(List<string>))
                {
                    List<string> list = new List<string>();
                    foreach (string value in node.GetValues(field.Name.ToString()))
                    {
                        list.Add(value);
                    }
                    field.SetValue(obj, list);
                }
                // Restore the dictionary by loading all the values in the sub-node:
                else if (field.FieldType == typeof(Dictionary<string, double>))
                {
                    Dictionary<string, double> dict = new Dictionary<string, double>();
                    ConfigNode dictNode = node.GetNode(field.Name.ToString());
                    foreach (ConfigNode.Value item in dictNode.values)
                    {
                        dict.Add(item.name, double.Parse(item.value));
                    }
                    field.SetValue(obj, dict);
                }
                // Load orbit via the helper-class:
                else if (field.FieldType == typeof(Orbit)) field.SetValue(obj, GUIOrbitEditor.CreateOrbitFromNode(node.GetNode(field.Name.ToString())));
                // Fallback; try to store the value as string:
                else field.SetValue(obj, node.GetValue(field.Name.ToString()));
            }
            return obj;
        }
    }

    [KSPAddon(KSPAddon.Startup.SpaceCentre, true)]
    public class KSTS : UnityEngine.MonoBehaviour
    {
        private static bool initialized = false;
        public static Dictionary<string, AvailablePart> partDictionary = null;
        public static Dictionary<string, PartResourceDefinition> resourceDictionary = null;

        // Is called when this Addon is first loaded to initializes all values (eg registration of event-handlers and creation
        // of original-stats library).
        public void Awake()
        {
            try
            {
                FlightRecoorder.Initialize();
                MissionController.Initialize();

                // Build dictionary of all parts for easier access:
                if (KSTS.partDictionary == null)
                {
                    KSTS.partDictionary = new Dictionary<string, AvailablePart>();
                    foreach (AvailablePart part in PartLoader.Instance.parts)
                    {
                        if (KSTS.partDictionary.ContainsKey(part.name.ToString()))
                        {
                            Debug.LogError("[KSTS] duplicate part-name '" + part.name.ToString() + "'");
                            continue;
                        }
                        KSTS.partDictionary.Add(part.name.ToString(), part);
                    }
                }

                // Build a dictionay of all resources for easier access:
                if (KSTS.resourceDictionary == null)
                {
                    KSTS.resourceDictionary = new Dictionary<string, PartResourceDefinition>();
                    foreach (PartResourceDefinition resourceDefinition in PartResourceLibrary.Instance.resourceDefinitions.ToList())
                    {
                        KSTS.resourceDictionary.Add(resourceDefinition.name.ToString(), resourceDefinition);
                    }
                }

                // Invoke the timer-function every second to run background-code:
                if (!IsInvoking("Timer"))
                {
                    InvokeRepeating("Timer", 1, 1);
                }

                // Execute the following code only once:
                if (KSTS.initialized) return;
                DontDestroyOnLoad(this);
                KSTS.initialized = true;
            }
            catch (Exception e)
            {
                Debug.LogError("[KSTS] Awake(): " + e.ToString());
            }
        }

        public void Timer()
        {
            try
            {
                // Don't update while not in game:
                if (HighLogic.LoadedScene == GameScenes.MAINMENU || HighLogic.LoadedScene == GameScenes.CREDITS || HighLogic.LoadedScene == GameScenes.SETTINGS) return;

                // Call all background-jobs:
                FlightRecoorder.Timer();
                MissionController.Timer();
            }
            catch (Exception e)
            {
                Debug.LogError("[KSTS] Timer(): " + e.ToString());
            }
        }
    }

    // This class handels load- and save-operations.
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.EDITOR, GameScenes.FLIGHT, GameScenes.TRACKSTATION, GameScenes.SPACECENTER)]
    class KSTSScenarioModule : ScenarioModule
    {
        public override void OnSave(ConfigNode node)
        {
            try
            {
                FlightRecoorder.SaveRecordings(node);
                MissionController.SaveMissions(node);
            }
            catch (Exception e)
            {
                Debug.LogError("[KSTS] OnSave(): " + e.ToString());
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            try
            {
                FlightRecoorder.LoadRecordings(node);
                MissionController.LoadMissions(node);
                GUI.Reset();
            }
            catch (Exception e)
            {
                Debug.LogError("[KSTS] OnLoad(): " + e.ToString());
            }
        }
    }
}
