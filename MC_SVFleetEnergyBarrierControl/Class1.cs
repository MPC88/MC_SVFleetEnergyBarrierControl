
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MC_SVFleetEnergyBarrierControl
{
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class Main : BaseUnityPlugin
    {
        public const string pluginGuid = "mc.starvalor.fleetenergybarriercontrol";
        public const string pluginName = "SV Fleet Energy Barrier Control";
        public const string pluginVersion = "1.0.4";

        private const string modSaveFolder = "/MCSVSaveData/";  // /SaveData/ sub folder
        private const string modSaveFilePrefix = "FleeetEBCntrl_"; // modSaveFilePrefixNN.dat
        private const int defaultThreshold = 3; // 1 = 10%, 2 = 20% etc.
        private static PersistentData data = null;
        private static GameObject emergencyWarpGO = null;
        private static GameObject energyBarrierGO = null;
        private static Dropdown energyBarrierDropdown = null;

        private static ManualLogSource log = BepInEx.Logging.Logger.CreateLogSource(pluginName);

        public void Awake()
        {
            Harmony.CreateAndPatchAll(typeof(Main));
        }

        [HarmonyPatch(typeof(AE_EnergyBarrier), nameof(AE_EnergyBarrier.ShouldBeActivated))]
        [HarmonyPostfix]
        private static void AE_EBShouldBeActive_Post(AE_EnergyBarrier __instance, AIControl aiControl, ref bool __result)
        {
            if (aiControl.Char is PlayerFleetMember &&
                data.thresholds.TryGetValue((aiControl.Char as PlayerFleetMember).crewMemberID, out int threshold))
                __result = __instance.ss.currHP < __instance.ss.baseHP * ((float)threshold / 10) && __instance.ss.fluxChargeSys.charges > 0 && __instance.cooldownRemaining <= 0f;
        }

        #region UI
        [HarmonyPatch(typeof(FleetBehaviorControl), nameof(FleetBehaviorControl.Open))]
        [HarmonyPrefix]
        private static void FBCOpen_Pre(FleetBehaviorControl __instance, GameObject ___emergencyWarpGO)
        {
            if (energyBarrierGO == null)
                CreateEmergencyWarpGO(__instance, ___emergencyWarpGO);
        }

        private static bool CreateEmergencyWarpGO(FleetBehaviorControl __instance, GameObject ___emergencyWarpGO)
        {
            if (___emergencyWarpGO != null)
                emergencyWarpGO = Instantiate(___emergencyWarpGO);
            else
                return false;

            energyBarrierGO = Instantiate(emergencyWarpGO);

            GameObject text = energyBarrierGO.transform.Find("Text").gameObject;
            text.GetComponentInChildren<Text>().text = "Activate Energy Barrier when HP below";

            energyBarrierDropdown = energyBarrierGO.transform.Find("Dropdown").GetComponent<Dropdown>();

            energyBarrierGO.transform.Find("Note").GetComponentInChildren<Text>().text = "";

            energyBarrierGO.transform.SetParent(___emergencyWarpGO.transform.parent, false);
            energyBarrierGO.transform.localPosition = new Vector3(
                ___emergencyWarpGO.transform.localPosition.x,
                ___emergencyWarpGO.transform.localPosition.y -
                text.GetComponent<RectTransform>().rect.height -
                energyBarrierDropdown.gameObject.GetComponent<RectTransform>().rect.height,
                ___emergencyWarpGO.transform.localPosition.z);
            energyBarrierGO.transform.localScale = ___emergencyWarpGO.transform.localScale;
            energyBarrierGO.layer = ___emergencyWarpGO.layer;

            Transform bg = __instance.transform.Find("BG");
            bg.localScale = new Vector3(
                bg.localScale.x,
                bg.localScale.y + 0.125f,
                bg.localScale.z);
            float diff = bg.GetComponent<RectTransform>().rect.yMin * 0.125f;

            for (int i = 1; i < __instance.transform.childCount - 2; i++)
            {
                Transform child = __instance.transform.GetChild(i);
                child.localPosition = new Vector3(
                    child.localPosition.x,
                    child.localPosition.y - diff,
                    child.localPosition.z);
            }
            Transform btnClose = __instance.transform.GetChild(__instance.transform.childCount - 2);
            btnClose.localPosition = new Vector3(
                btnClose.localPosition.x,
                btnClose.localPosition.y + diff,
                btnClose.localPosition.z);

            return true;
        }

        [HarmonyPatch(typeof(FleetBehaviorControl), nameof(FleetBehaviorControl.Open))]
        [HarmonyPostfix]
        private static void FBCOpen_Post(FleetBehaviorControl __instance, GameObject ___emergencyWarpGO, AIMercenaryCharacter ___aiMercChar)
        {
            if (data == null)
                data = new PersistentData();

            if (energyBarrierGO == null && !CreateEmergencyWarpGO(__instance, ___emergencyWarpGO))
                return;
            
            if (data.thresholds.Count != CountPlayerFleetMemebers())
            {                
                for (int i = 0; i < PChar.Char.mercenaries.Count; i++)
                    if (PChar.Char.mercenaries[i] is PlayerFleetMember &&
                        !data.thresholds.ContainsKey((PChar.Char.mercenaries[i] as PlayerFleetMember).crewMemberID))
                        data.thresholds.Add((PChar.Char.mercenaries[i] as PlayerFleetMember).crewMemberID, defaultThreshold);
            }

            if (___aiMercChar != null && ___aiMercChar is PlayerFleetMember)
            {
                int crewID = (___aiMercChar as PlayerFleetMember).crewMemberID;
                bool gotValue = data.thresholds.TryGetValue(crewID, out int curThreshold);

                if (!gotValue)
                {
                    data.thresholds.Add(crewID, defaultThreshold);
                    curThreshold = defaultThreshold;
                }

                Dropdown.DropdownEvent dde = new Dropdown.DropdownEvent();
                UnityAction<int> ua = null;
                ua += (int index) => ThresholdChanged(___aiMercChar as PlayerFleetMember);
                dde.AddListener(ua);
                energyBarrierDropdown.onValueChanged = dde;
                energyBarrierGO.transform.Find("Text").GetComponentInChildren<Text>().color = ColorSys.colWhite;
                energyBarrierGO.SetActive(true);
                energyBarrierDropdown.enabled = true;
                energyBarrierDropdown.value = curThreshold;
            }
            else
            {
                energyBarrierDropdown.onValueChanged = null;
                energyBarrierGO.transform.Find("Text").GetComponentInChildren<Text>().color = ColorSys.colDarkGray;
                energyBarrierGO.SetActive(false);
                energyBarrierDropdown.enabled = false;
            }
        }

        private static int CountPlayerFleetMemebers()
        {
            int result = 0;
            foreach (AIMercenaryCharacter aimc in PChar.Char.mercenaries)
                if (aimc is PlayerFleetMember)
                    result++;
            return result;
        }

        [HarmonyPatch(typeof(FleetBehaviorControl), nameof(FleetBehaviorControl.Close))]
        [HarmonyPostfix]
        private static void FBCClose_Post()
        {
            if (energyBarrierGO != null)
                energyBarrierGO.SetActive(false);
        }

        private static void ThresholdChanged(PlayerFleetMember aiChar)
        {
            if (data.thresholds != null &&
                data.thresholds.ContainsKey(aiChar.crewMemberID))
                data.thresholds[aiChar.crewMemberID] = energyBarrierDropdown.value;
        }
        #endregion

        #region PChar.char.mercenaries list changes
        [HarmonyPatch(typeof(AIMercenary), "SetActions")]
        [HarmonyPrefix]
        private static void AIMercSetActions_Pre(out int __state)
        {
            __state = PChar.Char.mercenaries.Count;
        }

        [HarmonyPatch(typeof(AIMercenary), "SetActions")]
        [HarmonyPrefix]
        private static void AIMercSetActions_Post(AIMercenary __instance, int __state)
        {
            if (PChar.Char.mercenaries.Count < __state &&
                __instance.aiMercChar is PlayerFleetMember &&
                data.thresholds != null &&                
                data.thresholds.ContainsKey((__instance.aiMercChar as PlayerFleetMember).crewMemberID))
                data.thresholds.Remove((__instance.aiMercChar as PlayerFleetMember).crewMemberID);
        }

        [HarmonyPatch(typeof(AIMercenary), nameof(AIMercenary.Die))]
        [HarmonyPrefix]
        private static void AIMercDie_Pre(out int __state)
        {
            __state = PChar.Char.mercenaries.Count;
        }

        [HarmonyPatch(typeof(AIMercenary), nameof(AIMercenary.Die))]
        [HarmonyPostfix]
        private static void AIMercDie_Post(AIMercenary __instance, int __state)
        {
            if (PChar.Char.mercenaries.Count < __state &&
                __instance.aiMercChar is PlayerFleetMember &&
                data.thresholds != null &&
                data.thresholds.ContainsKey((__instance.aiMercChar as PlayerFleetMember).crewMemberID))
                data.thresholds.Remove((__instance.aiMercChar as PlayerFleetMember).crewMemberID);
        }

        [HarmonyPatch(typeof(GameManager), nameof(GameManager.GenerateGameEvent))]
        [HarmonyPostfix]
        private static void GMGenerateGameEvent_Post(GameEvent ge)
        {
            if (ge.type == GameEventType.SpawnAlly && ge.par1 >= 0)
            {
                AICharacter aiChar = PChar.Char.mercenaries[PChar.Char.mercenaries.Count - 1];
                if (aiChar is PlayerFleetMember &&
                    data != null && 
                    !data.thresholds.ContainsKey((aiChar as PlayerFleetMember).crewMemberID))
                    data.thresholds.Add((aiChar as PlayerFleetMember).crewMemberID, defaultThreshold);
            }
        }

        [HarmonyPatch(typeof(GameManager), nameof(GameManager.CreatePlayerFleetMember))]
        [HarmonyPostfix]
        private static void GMCreatePlayerFleetMember_Post(CrewMember crewMember)
        {
            if (crewMember.aiChar is PlayerFleetMember &&
                data != null && 
                !data.thresholds.ContainsKey(crewMember.id))
                data.thresholds.Add(crewMember.id, defaultThreshold);
        }

        [HarmonyPatch(typeof(Inventory), "RemoveFromFleet")]
        [HarmonyPrefix]
        private static void InvRemoveFromFleet_Pre(Inventory __instance, int ___selectedItem)
        {
            if (__instance == null || __instance.currStation == null ||
                !(PChar.Char.mercenaries[___selectedItem] is PlayerFleetMember))
                return;

            AICharacter aiChar = PChar.Char.mercenaries[___selectedItem];
            if(aiChar is PlayerFleetMember &&
               data.thresholds != null &&
               data.thresholds.ContainsKey((aiChar as PlayerFleetMember).crewMemberID))
               data.thresholds.Remove((aiChar as PlayerFleetMember).crewMemberID);
        }
        #endregion

        #region save/load
        [HarmonyPatch(typeof(GameData), nameof(GameData.SaveGame))]
        [HarmonyPrefix]
        private static void GameDataSaveGame_Pre()
        {
            SaveGame();
        }

        private static void SaveGame()
        {
            if (data == null || data.thresholds.Count == 0)
                return;

            string tempPath = Application.dataPath + GameData.saveFolderName + modSaveFolder + "LOTemp.dat";

            if (!Directory.Exists(Path.GetDirectoryName(tempPath)))
                Directory.CreateDirectory(Path.GetDirectoryName(tempPath));

            if (File.Exists(tempPath))
                File.Delete(tempPath);

            BinaryFormatter binaryFormatter = new BinaryFormatter();
            FileStream fileStream = File.Create(tempPath);
            binaryFormatter.Serialize(fileStream, data);
            fileStream.Close();

            File.Copy(tempPath, Application.dataPath + GameData.saveFolderName + modSaveFolder + modSaveFilePrefix + GameData.gameFileIndex.ToString("00") + ".dat", true);
            File.Delete(tempPath);
        }

        [HarmonyPatch(typeof(MenuControl), nameof(MenuControl.LoadGame))]
        [HarmonyPostfix]
        private static void MenuControlLoadGame_Post()
        {
            LoadData(GameData.gameFileIndex.ToString("00"));
        }

        private static void LoadData(string saveIndex)
        {
            string modData = Application.dataPath + GameData.saveFolderName + modSaveFolder + modSaveFilePrefix + saveIndex + ".dat";
            try
            {
                if (!saveIndex.IsNullOrWhiteSpace() && File.Exists(modData))
                {
                    BinaryFormatter binaryFormatter = new BinaryFormatter();
                    FileStream fileStream = File.Open(modData, FileMode.Open);
                    PersistentData loadData = (PersistentData)binaryFormatter.Deserialize(fileStream);
                    fileStream.Close();

                    if (loadData == null)
                        data = new PersistentData();
                    else
                        data = loadData;
                }
                else
                    data = new PersistentData();
            }
            catch
            {
                SideInfo.AddMsg("<color=red>Fleet enegy barrier control mod load failed.</color>");
            }
        }
        #endregion
    }

    [Serializable]
    internal class PersistentData
    {
        internal Dictionary<int, int> thresholds;

        internal PersistentData()
        {
            thresholds = new Dictionary<int, int>();
        }
    }
}
