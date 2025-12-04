namespace LouveSystems.K2.BotLib
{
    using LouveSystems.K2.Lib;
    using MoonSharp.Interpreter;
    using MoonSharp.Interpreter.Loaders;
    using MoonSharp.Interpreter.Serialization;
    using MoonSharp.Interpreter.Serialization.Json;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Numerics;
    using System.Text;

    public class LuaComputerPlayerBehaviour : ComputerPlayerBehaviour
    {
        private struct LuaScriptLoader : IScriptLoader
        {
            private readonly IReadOnlyList<LuaFile> files;

            public LuaScriptLoader(IReadOnlyList<LuaFile> files)
            {
                this.files = files;
            }

            public object LoadFile(string file, Table globalContext)
            {
                for (int i = 0; i < files.Count; i++) {
                    if (files[i].name == file) {
                        return files[i].getText();
                    }
                }

                return null;
            }

            public string ResolveFileName(string filename, Table globalContext)
            {
                return filename;
            }

            public string ResolveModuleName(string modname, Table globalContext)
            {

                for (int i = 0; i < files.Count; i++) {
                    if (files[i].name == modname) {
                        return modname;
                    }
                }

                return null;
            }
        }

        protected delegate void NoParamNoReturnDelegate();
        protected delegate void OneParamNoReturnDelegate(DynValue word);
        protected delegate void TwoParamNoReturnDelegate(DynValue a, DynValue b);
        protected delegate void ThreeParamNoReturnDelegate(DynValue a, DynValue b, DynValue c);
        protected delegate void OneFloatParamNoReturnDelegate(float x);
        protected delegate DynValue GetterDelegate();
        protected delegate DynValue[] MultiGetterDelegate();
        protected delegate DynValue OneParamGetterDelegate(DynValue a);
        protected delegate DynValue[] OneParamMultiGetterDelegate(DynValue a);
        protected delegate DynValue TwoParamGetterDelegate(DynValue a, DynValue b);
        protected delegate DynValue ThreeParamGetterDelegate(DynValue a, DynValue b, DynValue c);

        public class MissingScriptException : System.Exception { public MissingScriptException(string str) : base(str) { } }
        public class ScriptErrorException : System.Exception { public ScriptErrorException(string str) : base(str) { } }
        public class MissingCriticalFunction : System.Exception
        {
            public List<string> MissingFunctions;
            public MissingCriticalFunction(List<string> missingFunctions) : base()
            {
                this.MissingFunctions = missingFunctions;
            }
        }

        private const string TAKE_ACTIONS_FUNC = "PLAY_TURN";
        private const string TAKE_ACTIONS_LATE_FUNC = "PLAY_TURN_LATE";
        private const string GET_PERSONAS_FUNC = "GET_PERSONAS";

        private readonly Dictionary<string, string> requirableFiles = new Dictionary<string, string>();

        private readonly string name;

        private readonly MoonSharp.Interpreter.Script script;

        private readonly string[] obligatoryFunctions = new[]
        {
            TAKE_ACTIONS_FUNC,
            GET_PERSONAS_FUNC
        };

        private readonly Dictionary<byte, List<Coroutine>> routines = new Dictionary<byte, List<Coroutine>>();

        private readonly List<ComputerPersona> personas = new List<ComputerPersona>();

        public LuaComputerPlayerBehaviour(int fileIndex, IReadOnlyList<LuaFile> allFiles)
        {
            name = Path.GetFileNameWithoutExtension(allFiles[fileIndex].name);
            script = new MoonSharp.Interpreter.Script(
                CoreModules.Preset_HardSandbox
                | CoreModules.LoadMethods
                | CoreModules.Coroutine
                | CoreModules.Metatables
            );

            script.Options.ScriptLoader = new LuaScriptLoader(allFiles);

            InjectGlobals();

            try {
                script.DoString(allFiles[fileIndex].getText(), codeFriendlyName: allFiles[fileIndex].name);
            }
            catch (MoonSharp.Interpreter.InterpreterException e) {

                Err($"While loading script named {allFiles[fileIndex].name}");
                Err($"While loading script content\n{allFiles[fileIndex].getText()}");
                Err($"All files: {string.Join(" ", allFiles.Select(o => o.name))}");
                Err($"All texts: {string.Join(" ", allFiles.Select(o => o.getText()))}");

                Err(e.DecoratedMessage);

                throw e;
            }

            CheckScriptValidity();

            GeneratePersonas();
        }

        public override IEnumerator<bool> TakeActionsLate(byte forPlayerIndex, GameSession session)
        {
            if (script.Globals.Get(TAKE_ACTIONS_LATE_FUNC).IsNilOrNan()) {
                if (routines.ContainsKey(forPlayerIndex)) {
                    routines[forPlayerIndex].Clear();
                }
                else {
                    routines.Add(forPlayerIndex, new List<Coroutine>());
                }

                DynValue api = MakeAPI(forPlayerIndex, session);
                api.Table.Set(PascalToSnake("localPlayerIndex"), DynValue.NewNumber(forPlayerIndex));

                try {
                    Execute(forPlayerIndex, TAKE_ACTIONS_LATE_FUNC, api);
                }
                catch (Exception e) {
                    Err(e.ToString());
                    throw e;
                }

                var it = PlayRoutinesToEnd(forPlayerIndex, MAX_TICKS_LATE_ACTIONS);
                bool shouldEnd = it.Current;
                while (it.MoveNext() && !shouldEnd) {
                    shouldEnd = it.Current;
                    yield return false;
                }
            }

            yield return true;
        }

        public override IEnumerator<bool> TakeActions(byte forPlayerIndex, GameSession session)
        {
            if (routines.ContainsKey(forPlayerIndex)) {
                routines[forPlayerIndex].Clear();
            }
            else {
                routines.Add(forPlayerIndex, new List<Coroutine>());
            }

            DynValue api = MakeAPI(forPlayerIndex, session);
            api.Table.Set(PascalToSnake("localPlayerIndex"), DynValue.NewNumber(forPlayerIndex));

            try {
                Execute(forPlayerIndex, TAKE_ACTIONS_FUNC, api);
                return PlayRoutinesToEnd(forPlayerIndex, MAX_TICKS_ACTIONS);
            }
            catch (InterpreterException iE) {
                Err(iE.DecoratedMessage);
                Err(iE.ToString());
                throw iE;
            }
            catch (Exception e) {
                Err(e.ToString());
                throw e;
            }
        }

        private IEnumerator<bool> PlayRoutinesToEnd(byte forPlayerIndex, int max)
        {
            int i = 0;
            while (true) {
                for (int routineIndex = 0; routineIndex < routines[forPlayerIndex].Count; routineIndex++) {
                    var routine = routines[forPlayerIndex][routineIndex];
                    try {
                        routine.Resume();
                    }
                    catch (InterpreterException iE) {
                        Err(iE.DecoratedMessage);
                        Err(iE.ToString());
                        break;
                    }
                    catch (Exception e) {
                        Err(e.ToString());
                        break;
                    }

                    if (routine.State == CoroutineState.Dead) {
                        routines[forPlayerIndex].Remove(routine);
                        routineIndex--;
                    }

                    i++;

                    if (i >= max) {
                        Log($"NOTICE: Max yields reached (>{max}) for player {forPlayerIndex}, interrupting by force");
                        break;
                    }
                }

                if (routines[forPlayerIndex].Count == 0) {
                    break;
                }

                if (i >= max) {
                    Log($"NOTICE: Max yields reached (>{max}) for player {forPlayerIndex}, interrupting by force");
                    break;
                }

                yield return false;
            }

            routines[forPlayerIndex].Clear();
            yield return true;
        }

        private void Execute(byte playerIndex, string functionName, params object[] args)
        {
            if (!script.Globals.Get(functionName).IsNilOrNan()) {
                var routine = script.CreateCoroutine(script.Globals[functionName]).Coroutine;

                try {
                    routine.Resume(args);
                }
                catch (InterpreterException e) {
                    Err(e.DecoratedMessage);
                }

                if (routine.State != CoroutineState.Dead) {
                    // We're not finished!
                    routines[playerIndex].Add(routine);
                }
            }
        }

        private void PrintTableRecursively(DynValue val, StringBuilder builder, int depth = 0)
        {
            string tabs = "";

            for (int i = 0; i < depth; i++) {
                tabs += "\t";
            }

            foreach (var key in val.Table.Keys) {

                var value = val.Table.Get(key);

                builder.Append(tabs);
                builder.Append(key.ToString());
                builder.Append(" = ");

                if (value.Type == DataType.Table) {
                    builder.AppendLine("{");
                    PrintTableRecursively(value, builder, depth + 1);
                    builder.Append(tabs + "}");
                }
                else if (value.Type == DataType.Function) {
                    builder.Append("void ()");
                }
                else if (value.Type == DataType.ClrFunction) {

                    builder.Append(value.Callback.Name + " ()");
                }
                else {
                    builder.Append(value.ToDebugPrintString());
                }

                builder.Append(",");
                builder.AppendLine();
            }
        }

        protected DynValue MakeAPI(byte forPlayerIndex, GameSession session)
        {
            // This is repeated at each decision time
            Table table = new Table(script);

            table["rules"] = MakeAPI(session.Rules);
            table["random"] = MakeAPI(session.ComputersRandom);
            table["player"] = MakeAPI(session.SessionPlayers[forPlayerIndex]);
            table["world"] = MakeAPI(session.CurrentGameState.world, session.Rules, session.SessionPlayers[forPlayerIndex].RealmIndex);
            table["voting"] = MakeAPI(session.CurrentGameState.voting);
            table["days_passed"] = session.CurrentGameState.daysPassed;
            table["days_before_next_council"] = session.CurrentGameState.daysRemainingBeforeNextCouncil;
            table["councils_passed"] = session.CurrentGameState.councilsPassed;

            table[PascalToSnake(nameof(session.EverybodyHasPlayed))] = ToLuaFunction(session.EverybodyHasPlayed);

            return DynValue.NewTable(table);
        }

        private DynValue MakeAPI(Voting voting)
        {
            Table table = new Table(script);

            table[PascalToSnake(nameof(voting.Result.wastedVotes))] = voting.Result.wastedVotes;

            {

                if (voting.Result.scores == null) {
                    table[PascalToSnake(nameof(voting.Result.scores))] = DynValue.Nil;
                }
                else {

                    Table results = new Table(script);
                    for (int i = 0; i < voting.Result.scores.Length; i++) {
                        Table data = new Table(script);
                        data.Set("total", Dyn(voting.Result.scores[i].totalVotes));

                        for (EVotingCriteria criteria = 0; criteria < EVotingCriteria.COUNT; criteria++) {
                            data.Set($"has_{PascalToSnake(criteria.ToString())}", Dyn(voting.Result.scores[i].wonCriterias.Contains(criteria)));
                        }

                        results.Set(voting.Result.scores[i].realmIndex, DynValue.NewTable(data));

                        table[PascalToSnake(nameof(voting.Result.scores))] = results;
                    }
                }
            }

            return DynValue.NewTable(table);
        }

        private DynValue MakeAPI(World world, GameRules rules, byte forRealmIndex)
        {
            Table result = new Table(script);

            DynValue[] toPosition(DynValue index)
            {
                Position pos = world.Position((int)index.Number);
                return new DynValue[] {
                    DynValue.NewNumber(pos.x),
                    DynValue.NewNumber(pos.y)
                };
            }

            DynValue getRegionLootableSilverWorth(DynValue region)
            {
                byte owningRealm = forRealmIndex;
                int regionIndex = (int)region.Number;

                return Dyn(world.GetRegionLootableSilverWorth(regionIndex, owningRealm));
            }

            DynValue getTerritoryOfRealm(DynValue realm)
            {
                Table table = new Table(script);

                byte realmIndex = (byte)realm.Number;
                List<int> regions = new List<int>();

                world.GetTerritoryOfRealm(realmIndex, regions);

                for (int i = 0; i < regions.Count; i++) {
                    table.Append(Dyn(regions[i]));
                }

                return DynValue.FromObject(script, table);
            }

            DynValue getAttackTargetsForRegion(DynValue region, DynValue canExtend)
            {
                Table table = new Table(script);

                int regionIndex = (int)region.Number;
                List<int> regions = new List<int>();

                bool canExtendAttack = canExtend.IsNotNil() && canExtend.Boolean;

                world.GetAttackTargetsForRegionNoAlloc(regionIndex, canExtendAttack, regions);

                for (int i = 0; i < regions.Count; i++) {
                    table.Append(Dyn(regions[i]));
                }

                return DynValue.FromObject(script, table);
            }

            DynValue getNeighboringRegions(DynValue region)
            {
                Table table = new Table(script);

                int regionIndex = (int)region.Number;
                List<int> regions = new List<int>();

                world.GetNeighboringRegions(regionIndex, regions);

                for (int i = 0; i < regions.Count; i++) {
                    table.Append(Dyn(regions[i]));
                }

                return DynValue.FromObject(script, table);
            }

            DynValue getCapitalOfRealm(DynValue realm)
            {
                if (world.GetCapitalOfRealm((byte)realm.Number, out int regionIndex)) {
                    return Dyn(regionIndex);
                }
                else {
                    return DynValue.Nil;
                }
            }

            DynValue getRealmsList()
            {
                Table table = new Table(script);

                for (byte i = 0; i < world.Realms.Count; i++) {
                    if (!world.IsCouncilRealm(i)) {
                        table.Append(Dyn(i));
                    }
                }

                return DynValue.NewTable(table);
            }

            DynValue getRegionList()
            {
                Table table = new Table(script);

                for (int i = 0; i < world.Regions.Count; i++) {
                    if (!world.Regions[i].inert) {
                        table.Append(Dyn(i));
                    }
                }

                return DynValue.NewTable(table);
            }

            DynValue isReinforcedAgainstAttacks(DynValue region)
            {
                int regionIndex = (int)region.Number;
                List<int> regions = new List<int>();

                if (world.Regions[regionIndex].IsReinforcedAgainstAttack(rules, world.GetRealmFaction(forRealmIndex))) {
                    return DynValue.False;
                }
                else {
                    return DynValue.True;
                }
            }

            DynValue canRegionBeTaken(DynValue region)
            {
                int regionIndex = (int)region.Number;
                List<int> regions = new List<int>();

                if (world.Regions[regionIndex].CannotBeTaken(rules, world.GetRealmFaction(forRealmIndex))) {
                    return DynValue.False;
                }
                else {
                    return DynValue.True;
                }
            }

            DynValue getRegionBuilding(DynValue region)
            {
                int regionIndex = (int)region.Number;
                List<int> regions = new List<int>();

                if (world.Regions[regionIndex].isOwned) {
                    return Dyn(world.Regions[regionIndex].buildings);
                }
                else {
                    return DynValue.Nil;
                }
            }

            DynValue getOwnerOfRegion(DynValue region)
            {
                int regionIndex = (int)region.Number;
                List<int> regions = new List<int>();

                if (world.Regions[regionIndex].GetOwner(out byte owner)) {
                    return Dyn(owner);
                }
                else {
                    return DynValue.Nil;
                }
            }

            DynValue realmHasFaction(DynValue realm, DynValue faction)
            {
                int realmIndex = (int)realm.Number;
                EFactionFlag factionFlags = (EFactionFlag)(int)faction.Number;

                try {
                    if (world.GetRealmFaction(realmIndex).HasFlagSafe(factionFlags)) {
                        return DynValue.True;
                    }
                    else {
                        return DynValue.False;
                    }
                }
                catch (Exception e) {
                    Err($"Parameter error (realm {realmIndex} / faction flags {factionFlags}): {e.Message}");
                    return DynValue.False;
                }
            }

            DynValue regionHasFaction(DynValue region, DynValue faction)
            {
                int regionIndex = (int)region.Number;
                EFactionFlag factionFlags = (EFactionFlag)(int)faction.Number;

                try {

                    if (world.GetRegionFaction(regionIndex).HasFlagSafe(factionFlags)) {
                        return DynValue.True;
                    }
                    else {
                        return DynValue.False;
                    }
                }
                catch (Exception e) {
                    Err($"Parameter error (realm {regionIndex} / faction flags {factionFlags}): {e.Message}");
                    return DynValue.False;
                }
            }

            result[PascalToSnake(nameof(world.Position))] = (OneParamMultiGetterDelegate)toPosition;
            result[PascalToSnake(nameof(world.CanRealmAttackRegion))] = ToLuaFunction<byte, int, bool>(world.CanRealmAttackRegion);
            result[PascalToSnake(nameof(world.GetRealmFaction))] = ToLuaFunction<int, EFactionFlag>(world.GetRealmFaction);
            result[PascalToSnake(nameof(world.GetRegionFaction))] = ToLuaFunction<int, EFactionFlag>(world.GetRegionFaction);
            result[PascalToSnake(nameof(world.GetRegionLootableSilverWorth))] = (OneParamGetterDelegate)getRegionLootableSilverWorth;
            result[PascalToSnake(nameof(world.GetRegionSilverWorth))] = ToLuaFunction<int, int>(world.GetRegionSilverWorth);
            result[PascalToSnake(nameof(world.IsCouncilRealm))] = ToLuaFunction<byte, bool>(world.IsCouncilRealm);
            result[PascalToSnake(nameof(world.IsCouncilRegion))] = ToLuaFunction<int, bool>(world.IsCouncilRegion);

            result[PascalToSnake(nameof(world.GetTerritoryOfRealm))] = (OneParamGetterDelegate)getTerritoryOfRealm;
            result[PascalToSnake(nameof(world.GetAttackTargetsForRegion))] = (TwoParamGetterDelegate)getAttackTargetsForRegion;
            result[PascalToSnake(nameof(world.GetNeighboringRegions))] = (OneParamGetterDelegate)getNeighboringRegions;

            result[PascalToSnake(nameof(world.GetCapitalOfRealm))] = (OneParamGetterDelegate)getCapitalOfRealm;
            result["get_region_owner"] = (OneParamGetterDelegate)getOwnerOfRegion;
            result[PascalToSnake(nameof(getRegionBuilding))] = (OneParamGetterDelegate)getRegionBuilding;
            result[PascalToSnake(nameof(canRegionBeTaken))] = (OneParamGetterDelegate)canRegionBeTaken;
            result["is_region_reinforced"] = (OneParamGetterDelegate)isReinforcedAgainstAttacks;

            result["get_regions"] = (GetterDelegate)getRegionList;
            result["get_realms"] = (GetterDelegate)getRealmsList;

            result[PascalToSnake(nameof(realmHasFaction))] = (TwoParamGetterDelegate)realmHasFaction;
            result[PascalToSnake(nameof(regionHasFaction))] = (TwoParamGetterDelegate)regionHasFaction;

            return DynValue.NewTable(result);
        }

        private DynValue Dyn<T>(T obj)
        {
            return DynValue.FromObject(script, obj);
        }

        private DynValue[] Dyn<T>(IReadOnlyList<T> obj)
        {
            DynValue[] arr = new DynValue[obj.Count];
            for (int i = 0; i < obj.Count; i++) {
                arr[i] = DynValue.FromObject(script, obj[i]);
            }

            return arr;
        }

        private TwoParamGetterDelegate ToLuaFunction<T1, T2, TResult>(Func<T1, T2, TResult> func)
        {
            return (DynValue val1, DynValue val2) => DynValue.FromObject(script, func(val1.ToObject<T1>(), val2.ToObject<T2>()));
        }

        private OneParamGetterDelegate ToLuaFunction<T, TResult>(Func<T, TResult> func)
        {
            return (DynValue val) => DynValue.FromObject(script, func(val.ToObject<T>()));
        }

        private TwoParamNoReturnDelegate ToLuaFunction<T1, T2>(Action<T1, T2> action)
        {
            return (DynValue val1, DynValue val2) => action(val1.ToObject<T1>(), val2.ToObject<T2>());
        }


        private OneParamNoReturnDelegate ToLuaFunction<T>(Action<T> action)
        {
            return (DynValue val) => action(val.ToObject<T>());
        }

        private NoParamNoReturnDelegate ToLuaFunction(Action action)
        {
            return () => action();
        }

        private GetterDelegate ToLuaFunction<T>(Func<T> func)
        {
            return () => DynValue.FromObject(script, func());
        }

        protected DynValue MakeAPI(SessionPlayer player)
        {
            Table table = new Table(script);

            DynValue[] isUnderAttack(DynValue regionIndex)
            {
                if (player.IsUnderAttack((int)regionIndex.Number, out int attackCount, out bool anyExtended)) {
                    return new DynValue[] {
                        DynValue.NewBoolean(true),
                        DynValue.NewNumber(attackCount),
                        DynValue.NewBoolean(anyExtended)
                    };
                }
                else {
                    return new DynValue[] {
                        DynValue.Nil, DynValue.Nil, DynValue.Nil
                    };
                }
            }

            DynValue anyAttackPlanned()
            {
                if (player.HasAnyAttackPlanned(out int count)) {
                    return DynValue.NewNumber(count);
                }
                else {
                    return DynValue.Nil;
                }
            }

            DynValue isBuildingSomething(DynValue regionIndex)
            {
                if (player.IsBuildingSomething((int)regionIndex.Number, out EBuilding building)) {
                    return DynValue.FromObject(script, building);
                }
                else {
                    return DynValue.Nil;
                }
            }

            DynValue[] getPlannedBuildings()
            {
                List<EBuilding> planned = new List<EBuilding>();
                if (player.GetPlannedConstructions(planned)) {
                    return planned.Select(o => DynValue.FromObject(script, o)).ToArray();
                }

                return new DynValue[0];
            }

            DynValue[] getPlannedAttacks()
            {
                List<RegionAttackRegionTransform> planned = new List<RegionAttackRegionTransform>();
                if (player.GetPlannedAttacks(planned)) {
                    Table[] tables = new Table[planned.Count];
                    DynValue[] values = new DynValue[tables.Length];
                    for (int i = 0; i < planned.Count; i++) {
                        tables[i] = new Table(script);
                        var attack = planned[i];

                        tables[i].Set(PascalToSnake("AttackingRegionIndex"), DynValue.NewNumber(attack.AttackingRegionIndex));
                        tables[i].Set(PascalToSnake("TargetRegionIndex"), DynValue.NewNumber(attack.targetRegionIndex));
                        tables[i].Set(PascalToSnake("Extended"), DynValue.NewBoolean(attack.isExtendedAttack));

                        values[i] = DynValue.NewTable(table);
                    }


                    return values;
                }

                return new DynValue[0];
            }

            table[PascalToSnake(nameof(player.AdminUpgradeIsPlanned))] = ToLuaFunction(player.AdminUpgradeIsPlanned);
            table[PascalToSnake(nameof(player.AnyDecisionsRemaining))] = ToLuaFunction(player.AnyDecisionsRemaining);
            table[PascalToSnake(nameof(player.CanExtendAttack))] = ToLuaFunction(player.CanExtendAttack);
            table[PascalToSnake(nameof(player.CanPayForFavours))] = ToLuaFunction(player.CanPayForFavours);
            table[PascalToSnake(nameof(player.CanUpgradeAdministration))] = ToLuaFunction(player.CanUpgradeAdministration);
            table[PascalToSnake(nameof(player.FavoursArePlanned))] = ToLuaFunction(player.FavoursArePlanned);
            table[PascalToSnake(nameof(player.GetAdministrationUpgradeSilverCost))] = ToLuaFunction(player.GetAdministrationUpgradeSilverCost);
            table[PascalToSnake(nameof(player.GetMaximumDecisions))] = ToLuaFunction(player.GetMaximumDecisions);
            table[PascalToSnake(nameof(player.GetRemainingDecisions))] = ToLuaFunction(player.GetRemainingDecisions);
            table[PascalToSnake(nameof(player.GetTreasury))] = ToLuaFunction(player.GetTreasury);
            table[PascalToSnake("IsFavoured")] = ToLuaFunction(player.IsLocalPlayerFavoured);

            table[PascalToSnake(nameof(player.CanAfford))] = ToLuaFunction<int, bool>(player.CanAfford);
            table[PascalToSnake(nameof(player.CanBuildOn))] = ToLuaFunction<int, bool>(player.CanBuildOn);
            table[PascalToSnake(nameof(player.CanBuild))] = ToLuaFunction<int, EBuilding, bool>(player.CanBuild);
            table[PascalToSnake(nameof(player.CanPlayWithRegion))] = ToLuaFunction<int, bool>(player.CanPlayWithRegion);
            table[PascalToSnake(nameof(player.GetPlannedAttacks))] = (MultiGetterDelegate)getPlannedAttacks;
            table["get_planned_buildings"] = (MultiGetterDelegate)getPlannedBuildings;
            table[PascalToSnake(nameof(player.HasAnyAttackPlanned))] = (GetterDelegate)anyAttackPlanned;
            table[PascalToSnake(nameof(player.IsBuildingAnything))] = (GetterDelegate)(() => DynValue.NewNumber(player.IsBuildingAnything(out int count) ? count : 0));
            table[PascalToSnake(nameof(player.IsBuildingSomething))] = (OneParamGetterDelegate)isBuildingSomething;
            table[PascalToSnake(nameof(player.IsUnderAttack))] = (OneParamMultiGetterDelegate)isUnderAttack;
            table[PascalToSnake(nameof(player.PayForFavours))] = ToLuaFunction(player.PayForFavours);
            table[PascalToSnake(nameof(player.UpgradeAdministration))] = ToLuaFunction(player.UpgradeAdministration);
            table[PascalToSnake(nameof(player.PlanAttack))] = ToLuaFunction<int, int>(player.PlanAttack);
            table[PascalToSnake(nameof(player.PlanConstruction))] = ToLuaFunction<int, EBuilding>(player.PlanConstruction);

            table[PascalToSnake(nameof(player.Faction))] = DynValue.FromObject(script, player.Faction);
            table[PascalToSnake(nameof(player.RealmIndex))] = DynValue.FromObject(script, player.RealmIndex);

            return DynValue.NewTable(table);
        }

        protected DynValue MakeAPI(GameRules rules)
        {
            // TODO
            return DynValue.Nil;

            //JsonSerializer.Serialize(rules);
            //string json = Newtonsoft.Json.JsonConvert.SerializeObject(rules);

            //DynValue val = DynValue.NewTable(JsonTableConverter.JsonToTable(json, script));

            //return val;
        }


        protected DynValue MakeAPI(ManagedRandom random)
        {
            Table table = new Table(script);

            table["int"] = (GetterDelegate)(() => DynValue.NewNumber(random.Next()));
            table["int_under"] = (OneParamGetterDelegate)((DynValue max) => DynValue.NewNumber(random.Next((int)max.Number)));

            return DynValue.NewTable(table);
        }

        protected void Log(DynValue value)
        {
            Log(value.ToDebugPrintString());
        }

        protected void Log(string val)
        {
            Console.WriteLine("LUA >>> " + val);
        }

        protected void Err(string str)
        {
            Console.WriteLine(str);
            throw new Exception(str);
        }

        protected virtual void InjectGlobals()
        {
            script.Globals["LOG"] = (OneParamNoReturnDelegate)Log;

            InjectEnum<EFactionFlag>();
            InjectEnum<EBuilding>();
            InjectEnum<EVotingCriteria>();
        }

        private void InjectEnum<T>() where T : struct, Enum
        {
            bool isString = !typeof(T).GetCustomAttributes(typeof(FlagsAttribute), true).Any();

            Script.GlobalOptions.CustomConverters.SetClrToScriptCustomConversion<T>(
                (script, v) => DynValue.NewString(v.ToString())
            );

            Script.GlobalOptions.CustomConverters.SetScriptToClrCustomConversion(
                isString ? DataType.String : DataType.Number,
                typeof(T),
                (dynVal) =>
                {
                    if (isString) {
                        bool findEnum(string str, out T parsed)
                        {
                            if (Enum.TryParse<T>(str, out parsed)) {
                                return true;
                            }

                            Err($"Unknown {typeof(T)} name {str}. Valid {typeof(T)} values are: {string.Join(", ", Enum.GetNames(typeof(T)))}");

                            return false;
                        }


                        if (findEnum(dynVal.String, out T castValue)) {
                            return castValue;
                        }
                    }
                    else {
                        if (Enum.IsDefined(typeof(T), (int)dynVal.Number)) {
                            return Enum.ToObject(typeof(T), (int)dynVal.Number);
                        }
                        else {
                            Err($"Unknown {typeof(T)} flag-set {(int)dynVal.Number}. Valid {typeof(T)} flags are: {string.Join(", ", Enum.GetValues(typeof(T)))}");
                        }
                    }

                    return default;
                }
            );


            var table = new Table(script);
            var values = Enum.GetValues(typeof(T));
            for (int i = 0; i < values.Length; i++) {
                T val = (T)values.GetValue(i);
                string str = val.ToString();
                table.Set(
                    PascalToSnake(str).ToUpper(),
                    isString ? DynValue.NewString(str) : DynValue.NewNumber((int)values.GetValue(i))
                );
            }

            script.Globals[typeof(T).Name.ToUpper()] = DynValue.NewTable(table);
        }

        private string PascalToSnake(string name)
        {
            StringBuilder snakeBuilder = new StringBuilder(char.ToLower(name[0]).ToString());

            for (int i = 1; i < name.Length; i++) {
                if (char.IsLower(name[i])) {
                    snakeBuilder.Append(name[i]);
                }
                else {
                    snakeBuilder.Append('_');
                    snakeBuilder.Append(char.ToLower(name[i]));
                }
            }

            return snakeBuilder.ToString();
        }

        private void CheckScriptValidity()
        {

            List<string> missingFunctions = new List<string>();
            foreach (var func in obligatoryFunctions) {
                if (script.Globals[func] == null || !(script.Globals[func] is Closure)) {
                    missingFunctions.Add(func);
                }
            }

            if (missingFunctions.Count > 0) {
                throw new MissingCriticalFunction(missingFunctions);
            }
        }

        public override string GetInternalName()
        {
            return name;
        }

        private void GeneratePersonas()
        {
            personas.Clear();

            if (script.Globals[GET_PERSONAS_FUNC] is Closure closure) {
                DynValue result = closure.Call();
                if (result.Type == DataType.Table) {
                    Table table = result.Table;

                    foreach (DynValue val in table.Values) {
                        if (val.Type == DataType.Table && val.Table is Table obj) {
                            ComputerPersona persona = new ComputerPersona();

                            persona.name = obj.Get("name").String;
                            persona.gender = (byte)obj.Get("gender").Number;

                            if (obj.Get("only_for_faction").IsNotNil()) {
                                persona.factionIndexFilter = (byte)obj.Get("only_for_faction").Number;
                            }

                            personas.Add(persona);
                        }
                    }
                }
            }

            if (personas.Count < MINIMUM_PERSONAS) {
                throw new Exception($"Not enough personas ({MINIMUM_PERSONAS} required, {personas.Count} found)");
            }
        }

        public override IReadOnlyList<ComputerPersona> GetPersonas()
        {
            return personas;
        }
    }
}
