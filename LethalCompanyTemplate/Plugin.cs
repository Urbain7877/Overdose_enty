using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using System.Collections;
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
            
            // Patch manuel un par un pour éviter tout scan global de classe par Harmony
            harmony.Patch(
                AccessTools.Method(typeof(RoundManager), "SpawnScrapInLevel"),
                prefix: new HarmonyMethod(typeof(ScrapBonusPatch), nameof(ScrapBonusPatch.BoostScrapAmount))
            );

            harmony.Patch(
                AccessTools.Method(typeof(EntranceTeleport), "TeleportPlayer"),
                postfix: new HarmonyMethod(typeof(EntrancePatch), nameof(EntrancePatch.Postfix))
            );

            harmony.Patch(
                AccessTools.Method(typeof(RoundManager), "Start"),
                postfix: new HarmonyMethod(typeof(RoundManagerStartPatch), nameof(RoundManagerStartPatch.Postfix))
            );

            harmony.Patch(
                AccessTools.Method(typeof(RoundManager), "Update"),
                postfix: new HarmonyMethod(typeof(RoundManagerUpdatePatch), nameof(RoundManagerUpdatePatch.Postfix))
            );

            harmony.Patch(
                AccessTools.Method(typeof(SandWormAI), "Update"),
                postfix: new HarmonyMethod(typeof(LeviathanIndoorPatch), nameof(LeviathanIndoorPatch.CustomLeviathanMovement))
            );
        }
    }

    // ==========================================
    // 1. RÈGLE : BONUS DE +20% DE SCRAP
    // ==========================================
    public class ScrapBonusPatch
    {
        public static void BoostScrapAmount(RoundManager __instance)
        {
            if (__instance.currentLevel != null)
            {
                __instance.currentLevel.minScrap = Mathf.RoundToInt(__instance.currentLevel.minScrap * 1.20f);
                __instance.currentLevel.maxScrap = Mathf.RoundToInt(__instance.currentLevel.maxScrap * 1.20f);
                Debug.Log($"[Monster-Overdose-Company] Bonus de scrap (+20%) appliqué ! Max scrap: {__instance.currentLevel.maxScrap}");
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

        public static void InitRobots(RoundManager manager)
        {
            spawnedRobots.Clear();
            hasSequenceStarted = false;

            if (manager.currentLevel == null || manager.currentLevel.OutsideEnemies == null) return;

            SpawnableEnemyWithRarity robotEnemy = manager.currentLevel.OutsideEnemies.Find(e => e.enemyType != null && e.enemyType.enemyName.ToLower().Contains("radmech"));
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
                    if (distanceToShip < 20f)
                    {
                        continue;
                    }

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
        public static void Postfix(RoundManager __instance)
        {
            ChaosManager.hasPlayerEntered = false;
            ChaosManager.gameTimer = 0f;
            ChaosManager.spawnIntervalTimer = 0f;
            RobotManager.InitRobots(__instance);
        }
    }

    public class RoundManagerUpdatePatch
    {
        public static void Postfix(RoundManager __instance)
        {
            if (!ChaosManager.hasPlayerEntered || __instance.currentLevel == null) return;

            ChaosManager.gameTimer += Time.deltaTime;
            ChaosManager.spawnIntervalTimer += Time.deltaTime;

            int currentMaxEnemies = 10 + (int)(ChaosManager.gameTimer / 120f) * 10;
            if (currentMaxEnemies > 60) currentMaxEnemies = 60;

            __instance.currentLevel.maxEnemyPowerCount = currentMaxEnemies;
            __instance.currentLevel.maxOutsideEnemyPowerCount = currentMaxEnemies;

            if (ChaosManager.spawnIntervalTimer >= 10f)
            {
                ChaosManager.spawnIntervalTimer = 0f;
                float chance = (ChaosManager.gameTimer < 120f) ? 0.30f : 0.85f;

                if (Random.value <= chance)
                {
                    TrySpawnChaosEnemy(__instance);
                }
            }

            if (ChaosManager.gameTimer >= 300f)
            {
                MakeAllEnemiesHostile();
            }
        }

        private static void TrySpawnChaosEnemy(RoundManager manager)
        {
            if (StartOfRound.Instance == null || StartOfRound.Instance.allPlayerScripts == null) return;

            PlayerControllerB targetPlayer = StartOfRound.Instance.allPlayerScripts[Random.Range(0, StartOfRound.Instance.allPlayerScripts.Length)];
            if (targetPlayer == null || !targetPlayer.isPlayerControlled || targetPlayer.isPlayerDead) return;

            List<SpawnableEnemyWithRarity> allEnemies = new List<SpawnableEnemyWithRarity>();
            if (manager.currentLevel.Enemies != null) allEnemies.AddRange(manager.currentLevel.Enemies);
            if (manager.currentLevel.OutsideEnemies != null) allEnemies.AddRange(manager.currentLevel.OutsideEnemies);

            if (allEnemies.Count == 0) return;

            SpawnableEnemyWithRarity selectedEnemy = allEnemies[Random.Range(0, allEnemies.Count)];
            if (selectedEnemy.enemyType == null) return;

            string enemyName = selectedEnemy.enemyType.enemyName.ToLower();

            bool isRobot = enemyName.Contains("radmech") || enemyName.Contains("old bird");
            bool isLeviathan = enemyName.Contains("sandworm");
            bool isInside = targetPlayer.isInsideFactory;

            if (isInside && isRobot) return;
            if (isLeviathan && ChaosManager.gameTimer < 420f) return;

            Vector3 spawnPos = targetPlayer.transform.position + (Random.insideUnitSphere * Random.Range(5f, 30f));

            NavMeshHit hit;
            if (NavMesh.SamplePosition(spawnPos, out hit, 30f, NavMesh.AllAreas))
            {
                int enemyIndex = manager.currentLevel.Enemies != null ? manager.currentLevel.Enemies.IndexOf(selectedEnemy) : -1;
                if (enemyIndex != -1)
                {
                    manager.SpawnEnemyOnServer(hit.position, 0f, enemyIndex);
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
