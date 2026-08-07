using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using GameNetcodeStuff;

namespace MonsterOverdoseCompany
{
    [BepInPlugin("com.votre_pseudo.monsteroverdosecompany", "Monster-Overdose-Company", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private readonly Harmony harmony = new Harmony("com.votre_pseudo.monsteroverdosecompany");
        public static Plugin Instance;

        void Awake()
        {
            Instance = this;
            Logger.LogInfo("[Monster-Overdose-Company] Mod chargé avec succès ! Préparez-vous au chaos.");
            
            try
            {
                // Utilisation de Assembly.GetType pour contourner le chargement statique des types corrompus de RoundManager
                Assembly assembly = Assembly.GetAssembly(typeof(GameNetworkManager));
                
                System.Type roundManagerType = assembly.GetType("RoundManager");
                System.Type entranceTeleportType = assembly.GetType("EntranceTeleport");
                System.Type sandWormAIType = assembly.GetType("SandWormAI");

                if (roundManagerType != null)
                {
                    MethodInfo spawnScrapMethod = roundManagerType.GetMethod("SpawnScrapInLevel", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (spawnScrapMethod != null)
                        harmony.Patch(spawnScrapMethod, prefix: new HarmonyMethod(typeof(ScrapBonusPatch), nameof(ScrapBonusPatch.BoostScrapAmount)));

                    MethodInfo startMethod = roundManagerType.GetMethod("Start", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (startMethod != null)
                        harmony.Patch(startMethod, postfix: new HarmonyMethod(typeof(RoundManagerStartPatch), nameof(RoundManagerStartPatch.Postfix)));

                    MethodInfo updateMethod = roundManagerType.GetMethod("Update", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (updateMethod != null)
                        harmony.Patch(updateMethod, postfix: new HarmonyMethod(typeof(RoundManagerUpdatePatch), nameof(RoundManagerUpdatePatch.Postfix)));
                }

                if (entranceTeleportType != null)
                {
                    MethodInfo teleportMethod = entranceTeleportType.GetMethod("TeleportPlayer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (teleportMethod != null)
                        harmony.Patch(teleportMethod, postfix: new HarmonyMethod(typeof(EntrancePatch), nameof(EntrancePatch.Postfix)));
                }

                if (sandWormAIType != null)
                {
                    MethodInfo wormUpdateMethod = sandWormAIType.GetMethod("Update", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (wormUpdateMethod != null)
                        harmony.Patch(wormUpdateMethod, postfix: new HarmonyMethod(typeof(LeviathanIndoorPatch), nameof(LeviathanIndoorPatch.CustomLeviathanMovement)));
                }

                Logger.LogInfo("[Monster-Overdose-Company] Tous les patchs dynamiques ont été appliqués avec succès !");
            }
            catch (System.Exception e)
            {
                Logger.LogError($"[Monster-Overdose-Company] Erreur lors du patch dynamique : {e}");
            }
        }
    }

    // ==========================================
    // 1. RÈGLE : BONUS DE +20% DE SCRAP
    // ==========================================
    public class ScrapBonusPatch
    {
        public static void BoostScrapAmount(object __instance)
        {
            if (__instance == null) return;
            System.Type type = __instance.GetType();
            FieldInfo currentLevelField = type.GetField("currentLevel");
            if (currentLevelField != null)
            {
                object currentLevel = currentLevelField.GetValue(__instance);
                if (currentLevel != null)
                {
                    System.Type levelType = currentLevel.GetType();
                    FieldInfo minScrapField = levelType.GetField("minScrap");
                    FieldInfo maxScrapField = levelType.GetField("maxScrap");

                    if (minScrapField != null && maxScrapField != null)
                    {
                        int min = (int)minScrapField.GetValue(currentLevel);
                        int max = (int)maxScrapField.GetValue(currentLevel);

                        minScrapField.SetValue(currentLevel, Mathf.RoundToInt(min * 1.20f));
                        maxScrapField.SetValue(currentLevel, Mathf.RoundToInt(max * 1.20f));
                        Debug.Log($"[Monster-Overdose-Company] Bonus de scrap (+20%) appliqué !");
                    }
                }
            }
        }
    }

    // ==========================================
    // 2. DÉCLENCHEURS (ENTRÉE ET SORTIE COMPLEXE)
    // ==========================================
    public class EntrancePatch
    {
        public static void Postfix(bool ___isEntranceToBuilding)
        {
            if (___isEntranceToBuilding && !ChaosManager.hasPlayerEntered)
            {
                ChaosManager.hasPlayerEntered = true;
                Debug.Log("[Monster-Overdose-Company] Joueur entre ! Chrono activé.");
            }
            else if (!___isEntranceToBuilding && ChaosManager.hasPlayerEntered && !RobotManager.hasSequenceStarted)
            {
                RobotManager.hasSequenceStarted = true;
                Plugin.Instance.StartCoroutine(RobotManager.WakeUpRobotsSequence());
                Debug.Log("[Monster-Overdose-Company] Joueur sort ! Lancement du réveil progressif des 25 robots !");
            }
        }
    }

    // ==========================================
    // 3. GESTION DES 25 ROBOTS
    // ==========================================
    public class RobotManager
    {
        public static List<RadMechAI> spawnedRobots = new List<RadMechAI>();
        public static bool hasSequenceStarted = false;

        public static void InitRobots(object managerObj)
        {
            spawnedRobots.Clear();
            hasSequenceStarted = false;
            if (managerObj == null) return;

            System.Type type = managerObj.GetType();
            FieldInfo currentLevelField = type.GetField("currentLevel");
            if (currentLevelField == null) return;

            object currentLevel = currentLevelField.GetValue(managerObj);
            if (currentLevel == null) return;

            System.Type levelType = currentLevel.GetType();
            FieldInfo outsideEnemiesField = levelType.GetField("OutsideEnemies");
            if (outsideEnemiesField == null) return;

            var outsideEnemies = outsideEnemiesField.GetValue(currentLevel) as System.Collections.IList;
            if (outsideEnemies == null) return;

            SpawnableEnemyWithRarity robotEnemy = null;
            foreach (var item in outsideEnemies)
            {
                if (item == null) continue;
                System.Type itemType = item.GetType();
                FieldInfo enemyTypeField = itemType.GetField("enemyType");
                if (enemyTypeField != null)
                {
                    object enemyTypeObj = enemyTypeField.GetValue(item);
                    if (enemyTypeObj != null)
                    {
                        System.Type enemyTypeClass = enemyTypeObj.GetType();
                        FieldInfo enemyNameField = enemyTypeClass.GetField("enemyName");
                        if (enemyNameField != null)
                        {
                            string name = enemyNameField.GetValue(enemyTypeObj) as string;
                            if (name != null && name.ToLower().Contains("radmech"))
                            {
                                robotEnemy = (SpawnableEnemyWithRarity)item;
                                break;
                            }
                        }
                    }
                }
            }

            if (robotEnemy == null) return;

            Vector3 shipPosition = Vector3.zero;
            GameObject shipObj = GameObject.FindWithTag("Ship");
            if (shipObj != null)
            {
                shipPosition = shipObj.transform.position;
            }
            else if (StartOfRound.Instance != null && StartOfRound.Instance.elevatorTransform != null)
            {
                shipPosition = StartOfRound.Instance.elevatorTransform.position;
            }

            int spawnedCount = 0;
            int attempts = 0;

            while (spawnedCount < 25 && attempts < 200)
            {
                attempts++;
                Vector3 randomPoint = 70f * Random.insideUnitSphere;
                NavMeshHit hit;

                if (NavMesh.SamplePosition(randomPoint, out hit, 50f, NavMesh.AllAreas))
                {
                    float distanceToShip = Vector3.Distance(hit.position, shipPosition);
                    if (distanceToShip < 20f) continue;

                    GameObject obj = Object.Instantiate(robotEnemy.enemyType.enemyPrefab, hit.position, Quaternion.identity);
                    RadMechAI robot = obj.GetComponent<RadMechAI>();
                    if (robot != null)
                    {
                        spawnedRobots.Add(robot);
                        spawnedCount++;
                    }
                }
            }
            Debug.Log($"[Monster-Overdose-Company] {spawnedCount} robots désactivés générés dehors.");
        }

        public static IEnumerator WakeUpRobotsSequence()
        {
            foreach (RadMechAI robot in spawnedRobots)
            {
                if (robot != null && !robot.isEnemyDead)
                {
                    robot.SwitchToBehaviourState(1); 
                    Debug.Log("[Monster-Overdose-Company] Un robot vient de se réveiller !");
                }
                yield return new WaitForSeconds(10f);
            }
        }
    }

    // ==========================================
    // 4. GESTION DU CHAOS
    // ==========================================
    public class ChaosManager
    {
        public static bool hasPlayerEntered = false;
        public static float gameTimer = 0f;
        public static float spawnIntervalTimer = 0f;
    }

    public class RoundManagerStartPatch
    {
        public static void Postfix(object __instance)
        {
            ChaosManager.hasPlayerEntered = false;
            ChaosManager.gameTimer = 0f;
            ChaosManager.spawnIntervalTimer = 0f;
            RobotManager.InitRobots(__instance);
        }
    }

    public class RoundManagerUpdatePatch
    {
        public static void Postfix(object __instance)
        {
            if (!ChaosManager.hasPlayerEntered || __instance == null) return;

            System.Type type = __instance.GetType();
            FieldInfo currentLevelField = type.GetField("currentLevel");
            if (currentLevelField == null) return;
            object currentLevel = currentLevelField.GetValue(__instance);
            if (currentLevel == null) return;

            ChaosManager.gameTimer += Time.deltaTime;
            ChaosManager.spawnIntervalTimer += Time.deltaTime;

            int currentMaxEnemies = 10 + (int)(ChaosManager.gameTimer / 120f) * 10;
            if (currentMaxEnemies > 60) currentMaxEnemies = 60;

            System.Type levelType = currentLevel.GetType();
            FieldInfo maxEnemyPowerField = levelType.GetField("maxEnemyPowerCount");
            FieldInfo maxOutsideEnemyPowerField = levelType.GetField("maxOutsideEnemyPowerCount");

            if (maxEnemyPowerField != null) maxEnemyPowerField.SetValue(currentLevel, currentMaxEnemies);
            if (maxOutsideEnemyPowerField != null) maxOutsideEnemyPowerField.SetValue(currentLevel, currentMaxEnemies);

            if (ChaosManager.spawnIntervalTimer >= 10f)
            {
                ChaosManager.spawnIntervalTimer = 0f;
                float chance = (ChaosManager.gameTimer < 120f) ? 0.30f : 0.85f;

                if (Random.value <= chance)
                {
                    TrySpawnChaosEnemy(__instance, currentLevel);
                }
            }

            if (ChaosManager.gameTimer >= 300f)
            {
                MakeAllEnemiesHostile();
            }
        }

        private static void TrySpawnChaosEnemy(object manager, object currentLevel)
        {
            if (StartOfRound.Instance == null || StartOfRound.Instance.allPlayerScripts == null) return;

            PlayerControllerB targetPlayer = StartOfRound.Instance.allPlayerScripts[Random.Range(0, StartOfRound.Instance.allPlayerScripts.Length)];
            if (targetPlayer == null || !targetPlayer.isPlayerControlled || targetPlayer.isPlayerDead) return;

            System.Type levelType = currentLevel.GetType();
            FieldInfo enemiesField = levelType.GetField("Enemies");
            FieldInfo outsideEnemiesField = levelType.GetField("OutsideEnemies");

            List<object> allEnemies = new List<object>();
            if (enemiesField != null)
            {
                var enList = enemiesField.GetValue(currentLevel) as System.Collections.IList;
                if (enList != null) foreach (var e in enList) allEnemies.Add(e);
            }
            if (outsideEnemiesField != null)
            {
                var outList = outsideEnemiesField.GetValue(currentLevel) as System.Collections.IList;
                if (outList != null) foreach (var e in outList) allEnemies.Add(e);
            }

            if (allEnemies.Count == 0) return;

            object selectedEnemy = allEnemies[Random.Range(0, allEnemies.Count)];
            if (selectedEnemy == null) return;

            System.Type enemyEntryType = selectedEnemy.GetType();
            FieldInfo enemyTypeField = enemyEntryType.GetField("enemyType");
            if (enemyTypeField == null) return;

            object enemyTypeObj = enemyTypeField.GetValue(selectedEnemy);
            if (enemyTypeObj == null) return;

            System.Type enemyTypeClass = enemyTypeObj.GetType();
            FieldInfo enemyNameField = enemyTypeClass.GetField("enemyName");
            if (enemyNameField == null) return;

            string enemyName = (enemyNameField.GetValue(enemyTypeObj) as string ?? "").ToLower();

            bool isRobot = enemyName.Contains("radmech") || enemyName.Contains("old bird");
            bool isLeviathan = enemyName.Contains("sandworm");
            bool isInside = targetPlayer.isInsideFactory;

            if (isInside && isRobot) return;
            if (isLeviathan && ChaosManager.gameTimer < 420f) return;

            Vector3 spawnPos = targetPlayer.transform.position + (Random.insideUnitSphere * Random.Range(5f, 30f));

            NavMeshHit hit;
            if (NavMesh.SamplePosition(spawnPos, out hit, 30f, NavMesh.AllAreas))
            {
                MethodInfo spawnMethod = manager.GetType().GetMethod("SpawnEnemyOnServer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (spawnMethod != null && enemiesField != null)
                {
                    var enList = enemiesField.GetValue(currentLevel) as System.Collections.IList;
                    int enemyIndex = enList != null ? enList.IndexOf(selectedEnemy) : -1;
                    if (enemyIndex != -1)
                    {
                        spawnMethod.Invoke(manager, new object[] { hit.position, 0f, enemyIndex });
                    }
                }
            }
        }

        private static void MakeAllEnemiesHostile()
        {
            EnemyAI[] enemies = Object.FindObjectsOfType<EnemyAI>();
            foreach (EnemyAI enemy in enemies)
            {
                if (enemy.isEnemyDead) continue;
                enemy.SwitchToBehaviourState(1);
            }
        }
    }

    // ==========================================
    // 5. RÈGLE LÉVIATHAN
    // ==========================================
    public class LeviathanIndoorPatch
    {
        public static void CustomLeviathanMovement(SandWormAI __instance)
        {
            if (__instance.targetPlayer != null && ChaosManager.gameTimer >= 420f)
            {
                if (__instance.agent == null)
                {
                    __instance.agent = __instance.gameObject.GetComponent<NavMeshAgent>();
                }

                if (__instance.agent != null && __instance.agent.isOnNavMesh)
                {
                    float distance = Vector3.Distance(__instance.transform.position, __instance.targetPlayer.transform.position);

                    if (distance > 20f)
                    {
                        __instance.agent.speed = 22f; 
                        __instance.SetDestinationToPosition(__instance.targetPlayer.transform.position);
                    }
                    else
                    {
                        __instance.agent.speed = 5f; 
                    }
                }
            }
        }
    }
}
