-- ZcDebug: the game side of `zcdebug`. A UE4SS Lua mod that answers one command at a time.
--
-- Channel (all in %LOCALAPPDATA%\ZeroCompanyEditor\debug, created by zcdebug):
--   request.txt    <id> TAB <command> TAB <args>      written by zcdebug
--   response.txt   <id> TAB ok|err TAB <text>         written here
--   heartbeat.txt  <unix seconds> TAB <info>          rewritten here about once a second
-- Backslash, newline, CR and tab inside a field are escaped as \\ \n \r \t (see Protocol.cs).
--
-- Commands: ping | find <Class> [limit] | props <object|Class> | get <object|Class> <Prop> |
--           set <object|Class> <Prop> <value> | exec <console command> | eval <lua>
-- An <object|Class> is a full object path (contains "/" or ".") or a short class name (its first live instance).
-- Nothing here changes the game on its own: set/exec/eval run only when zcdebug sends them (it asks for --write first).

local VERSION = "0.19"
local DIR = (os.getenv("LOCALAPPDATA") or ".") .. "\\ZeroCompanyEditor\\debug\\"
local REQUEST, RESPONSE, HEARTBEAT = DIR .. "request.txt", DIR .. "response.txt", DIR .. "heartbeat.txt"

-- Every load of this script is a "generation". `reload` runs the file again in the same Lua state: the new generation unregisters the old
-- one's hooks and the old one's loops see they are stale and stop, so nothing is doubled and no Ctrl+R is needed.
local GEN = (_G.ZC_GEN or 0) + 1
_G.ZC_GEN = GEN
for _, h in ipairs(_G.ZC_HOOKS or {}) do pcall(UnregisterHook, h[1], h[2], h[3]) end
_G.ZC_HOOKS = {}
local function alive() return _G.ZC_GEN == GEN end
local SCRIPT = (debug.getinfo(1, "S").source or ""):match("^@(.+)$")

-- RegisterHook that remembers its ids, so the next generation (or `trace off`) can remove it
local function trackedHook(path, fn, postFn)
    local ok, pre, post
    if postFn then ok, pre, post = pcall(RegisterHook, path, fn, postFn) else ok, pre, post = pcall(RegisterHook, path, fn) end
    if ok then _G.ZC_HOOKS[#_G.ZC_HOOKS + 1] = { path, pre, post } end
    return ok, pre, post
end

local lastId = nil
local busy = false
local busySince = 0          -- when the request now on the game thread was handed over

-- ---- escaping ----------------------------------------------------------------------------
local function esc(s)
    s = tostring(s)
    s = s:gsub("\\", "\\\\"):gsub("\n", "\\n"):gsub("\r", "\\r"):gsub("\t", "\\t")
    return s
end

local function unesc(s)
    return (s:gsub("\\(.)", function(c)
        if c == "n" then return "\n" elseif c == "r" then return "\r" elseif c == "t" then return "\t" end
        return c
    end))
end

-- ---- files -------------------------------------------------------------------------------
local function readAll(path)
    local f = io.open(path, "rb")
    if not f then return nil end
    local s = f:read("*a")
    f:close()
    return s
end

local function writeAtomic(path, text)
    local tmp = path .. ".tmp"
    local f = io.open(tmp, "wb")
    if not f then return false end
    f:write(text)
    f:close()
    os.remove(path)
    return os.rename(tmp, path)
end

local function reply(id, ok, text)
    writeAtomic(RESPONSE, id .. "\t" .. (ok and "ok" or "err") .. "\t" .. esc(text) .. "\n")
end

-- ---- reflection helpers ------------------------------------------------------------------
local function fmt(v, depth)
    depth = depth or 0
    local t = type(v)
    if v == nil then return "nil" end
    if t == "number" or t == "boolean" then return tostring(v) end
    if t == "string" then return v end
    if t == "table" then
        local parts = {}
        for k, x in pairs(v) do
            parts[#parts + 1] = tostring(k) .. "=" .. (depth < 2 and fmt(x, depth + 1) or "...")
        end
        return "{" .. table.concat(parts, ", ") .. "}"
    end
    -- userdata: an object, a name, a string or an array. A NULL object must never get another method call: UE4SS dereferences it
    -- (an access violation at address 0x10 that takes the whole game down), so objects are asked IsValid() first.
    local okv, valid = pcall(function() return v:IsValid() end)
    if okv and valid == false then return "(null object)" end
    local ok, s = pcall(function() return v:ToString() end)
    if ok and s then return tostring(s) end
    ok, s = pcall(function() return v:GetFullName() end)
    if ok and s then return tostring(s) end
    ok, s = pcall(function() return "[array of " .. tostring(#v) .. "]" end)
    if ok and s then return s end
    return "<" .. t .. ">"
end

local function resolve(spec)
    if spec:find("[/%.]") then
        local o = StaticFindObject(spec)
        if o and o:IsValid() then return o end
        return nil, "no object at " .. spec
    end
    local o = FindFirstOf(spec)
    if o and o:IsValid() then return o end
    return nil, "no live instance of class " .. spec
end

local function splitArgs(s, n)
    local out, rest = {}, s
    for i = 1, n - 1 do
        local a, b = rest:match("^%s*(%S+)%s*(.*)$")
        if not a then break end
        out[i], rest = a, b
    end
    out[#out + 1] = rest
    return out
end

local function coerce(v)
    if v == "true" then return true elseif v == "false" then return false end
    return tonumber(v) or v
end

-- ---- commands ----------------------------------------------------------------------------
local commands = {}

function commands.ping()
    return "pong " .. os.time() .. " zcdebug-mod " .. VERSION
end

function commands.find(args)
    local a = splitArgs(args, 2)
    local limit = tonumber(a[2]) or 25
    local list = FindAllOf(a[1])
    if not list then return "none" end
    local out = { #list .. " instance(s) of " .. a[1] }
    for i, o in ipairs(list) do
        if i > limit then out[#out + 1] = "..."; break end
        out[#out + 1] = o:GetFullName()
    end
    return table.concat(out, "\n")
end

function commands.props(args)
    local o, err = resolve(args:match("^%s*(%S+)"))
    if not o then error(err) end
    local out = { o:GetFullName() }
    -- ForEachProperty lists only the class's own properties; walk up to include inherited ones
    local c = o:GetClass()
    while c and c:IsValid() do
        local owner = c:GetFName():ToString()
        c:ForEachProperty(function(p)
            local ty = "?"
            pcall(function() ty = p:GetClass():GetFName():ToString() end)
            out[#out + 1] = p:GetFName():ToString() .. " : " .. ty .. "  (" .. owner .. ")"
        end)
        local ok, super = pcall(function() return c:GetSuperStruct() end)
        if not ok then break end
        c = super
    end
    return table.concat(out, "\n")
end

-- The declared type of a property of an object (and the element type for arrays), found without reading its value.
local function propType(obj, name)
    local okc, c = pcall(function() return obj:GetClass() end)
    local ty, inner
    while okc and c and c:IsValid() and not ty do
        c:ForEachProperty(function(p)
            if not ty and p:GetFName():ToString() == name then
                pcall(function() ty = p:GetClass():GetFName():ToString() end)
                if ty == "ArrayProperty" then pcall(function() inner = p:GetInner():GetClass():GetFName():ToString() end) end
            end
        end)
        local ok, super = pcall(function() return c:GetSuperStruct() end)
        if not ok then break end
        c = super
    end
    return ty, inner
end

local SAFE_INNER = { ObjectProperty = true, StructProperty = true, IntProperty = true, FloatProperty = true, DoubleProperty = true, BoolProperty = true }

-- Why a property must not be read, or nil. Arrays of soft object paths crashed the game (twice); so do not read arrays of types we
-- have not checked, maps, sets, or soft/lazy references. An unknown type (a struct's own field) is allowed.
local function whyUnsafe(ty, inner)
    if ty == nil then return nil end
    if ty == "ArrayProperty" and not (inner and SAFE_INNER[inner]) then return "an array of " .. tostring(inner or "an unknown element type") end
    if ty == "MapProperty" or ty == "SetProperty" or ty:find("Soft", 1, true) or ty:find("Lazy", 1, true) then return "a " .. ty end
    return nil
end

function commands.get(args)
    local a = splitArgs(args, 2)
    local o, err = resolve(a[1])
    if not o then error(err) end
    -- a path like SpawnedAttributes.1.Health: numbers index arrays (1-based), names read properties. Each property's declared
    -- type is checked BEFORE its value is read: reading arrays of soft object paths crashed the game twice.
    local v = o
    for part in a[2]:gmatch("[^%.]+") do
        local n = tonumber(part)
        if not n and type(v) == "userdata" then
            local ty, inner = propType(v, part)
            local bad = whyUnsafe(ty, inner)
            if bad then error("refusing to read '" .. part .. "': it is " .. bad .. " (reading that kind of property has crashed the game)") end
        end
        local ok, nxt = pcall(function() if n then return v[n] end return v[part] end)
        if not ok then error("cannot read '" .. part .. "': " .. tostring(nxt)) end
        v = nxt
        if v == nil then error("'" .. part .. "' is nil") end
    end
    -- an array with no index: list its elements (only arrays of safe element types get this far)
    local ok, count = pcall(function() return #v end)
    if type(v) == "userdata" and ok and type(count) == "number" and not pcall(function() return v:GetFullName() end) then
        local out = { "[array of " .. count .. "]" }
        for i = 1, math.min(count, 50) do out[#out + 1] = i .. ": " .. fmt(v[i]) end
        return table.concat(out, "\n")
    end
    return fmt(v)
end

-- every simple-valued property of an object (own and inherited) with its current value
function commands.dump(args)
    local o, err = resolve(args:match("^%s*(%S+)"))
    if not o then error(err) end
    local out = { o:GetFullName() }
    local c = o:GetClass()
    -- Only plain values are read, decided from the declared type before any read. Attribute sets also expand their attribute structs.
    local SIMPLE = { BoolProperty = true, IntProperty = true, Int64Property = true, UInt16Property = true, UInt32Property = true, UInt64Property = true,
                     FloatProperty = true, DoubleProperty = true, ByteProperty = true, EnumProperty = true, NameProperty = true, StrProperty = true }
    local isAttributeSet = c:GetFName():ToString():find("Set$") ~= nil
    while c and c:IsValid() do
        local owner = c:GetFName():ToString()
        c:ForEachProperty(function(p)
            local name = p:GetFName():ToString()
            local ty = "?"
            pcall(function() ty = p:GetClass():GetFName():ToString() end)
            if not (SIMPLE[ty] or (ty == "StructProperty" and isAttributeSet)) then return end
            local ok, v = pcall(function() return o[name] end)
            if ok and v ~= nil and (type(v) ~= "userdata") then
                out[#out + 1] = name .. " = " .. fmt(v) .. "  (" .. ty .. ", " .. owner .. ")"
            elseif ok and ty == "StructProperty" and v ~= nil then
                -- a GameplayAttributeData: show base and current value
                local okb, base = pcall(function() return v.BaseValue end)
                local okc, cur = pcall(function() return v.CurrentValue end)
                if okb and okc and type(base) == "number" then
                    out[#out + 1] = name .. " base=" .. fmt(base) .. " cur=" .. fmt(cur) .. "  (" .. owner .. ")"
                end
            end
        end)
        local ok, super = pcall(function() return c:GetSuperStruct() end)
        if not ok then break end
        c = super
    end
    return table.concat(out, "\n")
end

function commands.set(args)
    local a = splitArgs(args, 3)
    local o, err = resolve(a[1])
    if not o then error(err) end
    o[a[2]] = coerce(a[3])
    return "set " .. a[2] .. " = " .. fmt(o[a[2]])
end

-- attr <unit> <Attribute> <value>: write a gameplay attribute (base and current) on one unit, then
-- update the UI view-models that show it and broadcast the change, so the HUD follows the logic.
function commands.attr(args)
    local a = splitArgs(args, 3)
    local unit, err = resolve(a[1])
    if not unit then error(err) end
    local name, value = a[2], tonumber(a[3])
    if not name or name == "" or not value then error("usage: attr <unit> <Attribute> <number>") end

    local asc = unit.AbilitySystemComponent
    if not (asc and asc:IsValid()) then error("no AbilitySystemComponent on " .. unit:GetFullName()) end
    local sets, done = asc.SpawnedAttributes, nil
    for i = 1, #sets do
        local set = sets[i]
        local ok, data = pcall(function() return set[name] end)
        if ok and data ~= nil then
            local okb, base = pcall(function() return data.BaseValue end)
            if okb and type(base) == "number" then
                data.BaseValue = value
                data.CurrentValue = value
                done = set:GetFName():ToString() .. "." .. name
                break
            end
        end
    end
    if not done then error("no attribute '" .. name .. "' on " .. unit:GetFullName()) end

    local fullName, views = unit:GetFullName(), 0
    for _, v in ipairs(FindAllOf("BrunoGameplayAttributeViewModel") or {}) do
        local ok = pcall(function()
            if v.ContextObject:GetFullName() == fullName and v.CurrentValueAttribute.AttributeName:ToString() == name then
                v.CurrentValue = value
                if value > v.MaxValue then v.MaxValue = value end
                v:K2_BroadcastFieldValueChanged({ FieldName = FName("MaxValue") })
                v:K2_BroadcastFieldValueChanged({ FieldName = FName("CurrentValue") })
                v:K2_BroadcastFieldValueChanged({ FieldName = FName("NormalizedValue") })    -- health bars bind to this one
                views = views + 1
            end
        end)
    end
    return done .. " = " .. value .. " (" .. views .. " UI view-model(s) updated)"
end

-- One unit's ability system. Struct arrays are only ever walked for a single named unit and with
-- guards: looping over every character crashed the game once.
local function unitAsc(spec)
    local unit, err = resolve(spec)
    if not unit then error(err) end
    local asc = unit.AbilitySystemComponent
    if not (asc and asc:IsValid()) then error("no AbilitySystemComponent on " .. unit:GetFullName()) end
    return asc
end

local function effectNames(asc)
    local ge, names = asc.ActiveGameplayEffects.GameplayEffects_Internal, {}
    for i = 1, math.min(#ge, 300) do
        local ok, n = pcall(function()
            local d = ge[i].Spec.Def
            if d and d:IsValid() then
                local n = d:GetFName():ToString():gsub("^Default__GE_", ""):gsub("_C$", "")
                local stacks = ge[i].Spec.StackCount
                if type(stacks) == "number" and stacks > 1 then n = n .. " x" .. stacks end   -- stackable effects (Burning ...)
                return n
            end
            return "?"
        end)
        names[i] = ok and n or "?"
    end
    return names
end

function commands.effects(args)
    local names = effectNames(unitAsc(args:match("^%s*(%S+)")))
    return #names .. " effect(s)" .. (#names > 0 and ("\n" .. table.concat(names, "\n")) or "")
end

-- fx <unit> add|remove <Name | /full/class/path>: Name is a status effect such as Burning, Spotted
function commands.fx(args)
    local a = splitArgs(args, 3)
    local asc = unitAsc(a[1])
    local verb = a[2]
    local name, count = (a[3] or ""):match("^(%S+)%s*(%d*)")
    count = math.max(1, math.min(tonumber(count) or 1, 20))
    if (verb ~= "add" and verb ~= "remove") or not name then error("usage: fx <unit> add|remove <Name> [count]") end
    local cls
    if name:find("/") then
        cls = StaticFindObject(name)
    else
        local base = name:gsub("^GE_", ""):gsub("_C$", "")
        cls = StaticFindObject("/Game/Game/GameData/Abilities/ResultEffects/StatusEffects/GE_" .. base .. ".GE_" .. base .. "_C")
    end
    if not (cls and cls:IsValid()) then error("effect class not found: " .. name .. " (give a full class path if it is not a status effect)") end
    if verb == "add" then
        -- each application is one stack; stackable effects (Burning) merge into one entry up to their limit
        for _ = 1, count do asc:BP_ApplyGameplayEffectToSelf(cls, 1.0, asc:MakeEffectContext()) end
    else
        asc:RemoveActiveGameplayEffectBySourceEffect(cls, nil, -1)    -- removes every stack
    end
    local after = effectNames(asc)
    local shown = {}
    for _, n in ipairs(after) do if n:find(name, 1, true) then shown[#shown + 1] = n end end
    return verb .. " " .. name .. (verb == "add" and (" x" .. count) or "") .. ": " .. #after .. " effect(s); now " ..
        (#shown > 0 and table.concat(shown, ", ") or "none of it")
end

-- load <save name>: ask the game to load a save by file name (without .sav). The game only accepts names it listed when it
-- started: a file added while it runs is refused, but a known file whose contents were overwritten on disk loads the new data.
local lastLoad = 0
local loadMarker = 0   -- address of the tactical game state when the last load started: a finished load has a different one
local loadHub = {}     -- addresses of the Den's heroes when the last load started: a finished hub load has new ones

-- the Den's heroes (the Den is a sublevel of the one persistent world, so the World itself never changes)
local function hubHeroes()
    local found = {}
    for _, c in ipairs(FindAllOf("Character") or {}) do
        if c:IsValid() and c:GetFullName():find("HUB_Root.HUB_Root:PersistentLevel.Char_Hero_", 1, true) then
            found[c:GetAddress()] = true
        end
    end
    return found
end

local function tacticalState()
    local g = FindFirstOf("BP_BrunoTacticalGameState_C")
    if g and g:IsValid() then return g end
end

function commands.load(args)
    local name = args:match("^%s*(.-)%s*$"):gsub("%.sav$", "")
    if name == "" then error("usage: load <save file name>") end
    -- a second request while a load runs (even a refused one) left the level without its actors or input once
    local wait = 30 - (os.time() - lastLoad)
    if wait > 0 then error("a load was started " .. (30 - wait) .. "s ago; wait " .. wait .. "s so the game can finish it") end
    local subsystem = FindFirstOf("BrunoSaveGameSubsystem")
    if not (subsystem and subsystem:IsValid()) then error("the game's save subsystem was not found") end
    local before = tacticalState()
    if subsystem:LoadFile(name) then
        lastLoad = os.time()
        loadMarker = before and before:GetAddress() or 0
        loadHub = hubHeroes()
        return "loading " .. name
    end
    return "refused: the game does not know a save called '" .. name .. "' (names are listed once, at start-up; overwrite a known file instead)"
end

-- loadstatus: "ready ..." once the level of the last load is up (a new tactical game state that can be read, with heroes in
-- the level); otherwise "loading ..." with what is still missing. Reads only; zcdebug load polls it.
function commands.loadstatus()
    if lastLoad == 0 then return "none: no load was started since the mod started" end
    local g = tacticalState()
    if not g then
        -- the Den has no tactical game state: a hub load is up once it has heroes that were not there when the load started
        local n = 0
        for addr in pairs(hubHeroes()) do if not loadHub[addr] then n = n + 1 end end
        if n > 0 then return "ready: the Den, " .. n .. " hero(es) in the level" end
        return "loading: no tactical game state yet"
    end
    if g:GetAddress() == loadMarker then return "loading: the previous level is still there" end
    local ok, round = pcall(function() return g.CurrentRound end)
    if not ok or math.type(round) ~= "integer" then return "loading: the game state cannot be read yet" end
    local heroes = 0
    for _, c in ipairs(FindAllOf("Character") or {}) do
        if c:IsValid() then
            local full = c:GetFullName()
            if full:find("PersistentLevel.Char_Hero_", 1, true) then heroes = heroes + 1 end
        end
    end
    if heroes == 0 then return "loading: no heroes in the level yet" end
    return "ready: round " .. round .. ", " .. heroes .. " hero(es) in the level"
end

function commands.exec(args)
    local world = FindFirstOf("World")
    local pc = FindFirstOf("PlayerController")
    local ksl = StaticFindObject("/Script/Engine.Default__KismetSystemLibrary")
    if not (ksl and ksl:IsValid()) then error("KismetSystemLibrary not found") end
    ksl:ExecuteConsoleCommand(world, args, pc)
    return "sent: " .. args
end

function commands.eval(args)
    local chunk, err = load("return " .. args, "zcdebug", "t", _ENV or _G)
    if not chunk then chunk, err = load(args, "zcdebug", "t", _ENV or _G) end
    if not chunk then error(err) end
    local results = table.pack(chunk())
    local out = {}
    for i = 1, results.n do out[i] = fmt(results[i]) end
    return #out == 0 and "(no result)" or table.concat(out, "\t")
end

-- ---- combat log --------------------------------------------------------------------------
-- log on|off|clear|snap: while on, the game's own events are written to combat.log. Nothing polls: every line comes from a hook on an event the
-- game raises (a unit damaged, killed, moving, a shot fired, an ability used, a turn or a round starting), so the log costs nothing between
-- events. At the start of each round, and on `log snap`, one snapshot of every unit's health, armor, shields, action points and position is
-- written. `zcdebug log show [n]` prints the end of the file.
local COMBAT_LOG = DIR .. "combat.log"
local logging = _G.ZC_LOGGING == true     -- a reload keeps the log on

local function logLine(text)
    local f = io.open(COMBAT_LOG, "ab")
    if f then f:write(os.date("%H:%M:%S") .. "  " .. text .. "\n"); f:close() end
end

local function num(v) return type(v) == "number" and v or nil end
local function g(v) return v and string.format("%g", math.floor(v * 100 + 0.5) / 100) or "?" end

-- every name read goes through IsValid first: a method call on a null object takes the game down
-- UE4SS hands an event's object arguments either as the object itself or wrapped (a "param" with :get()): obj() gives the valid object or nil
local function obj(p)
    if p == nil then return nil end
    local ok, valid = pcall(function() return p:IsValid() end)
    if ok then return valid and p or nil end                            -- an object: usable, or null
    local ok2, o = pcall(function() return p:get() end)                -- a wrapped one
    if ok2 and o ~= nil then
        local ok3, v3 = pcall(function() return o:IsValid() end)
        if ok3 then return v3 and o or nil end
        return o
    end
    return nil
end

local function who(p)           -- the name of an object parameter
    local ok, v = pcall(function() local o = obj(p); if o then return o:GetFName():ToString() end end)
    return (ok and v) or "?"
end

local function abilityOf(p)     -- the ability's class name, e.g. GA_StandardShot_BlasterRifle_C
    local ok, v = pcall(function() local o = obj(p); if o then return o:GetClass():GetFName():ToString() end end)
    return (ok and v) or "?"
end

local function payload(p)       -- the struct an event passes (UE4SS wraps it; its fields read by name)
    local ok, o = pcall(function() return p:get() end)
    return ok and o or nil
end

local function field(o, name)
    if o == nil then return nil end
    local ok, v = pcall(function() return o[name] end)
    return ok and v or nil
end

-- an event argument as text: a gameplay tag by its name, an object by its name, an objective/bond/actor struct by what it points at, else as eval would print it
local function describeParam(p)
    local o = payload(p)
    if o == nil then
        -- an array can arrive without a wrapper: read it directly
        local okn, n = pcall(function() return #p end)
        if okn and type(n) == "number" and n >= 0 and n < 1000 then
            local names = {}
            for i = 1, math.min(n, 6) do names[#names + 1] = who(p[i]) end
            return "[" .. table.concat(names, ", ") .. (n > 6 and ", ..." or "") .. "]"
        end
        return fmt(p)
    end
    local tag = field(o, "TagName")
    if tag ~= nil then local ok, s = pcall(function() return tag:ToString() end); if ok then return "tag " .. tostring(s) end end
    for _, f in ipairs({ "TacticalObjective", "MissionObjective", "ObjectiveBase", "Actor", "SpawnedUnit" }) do
        local v = field(o, f)
        if v ~= nil then return f .. "=" .. who(v) end
    end
    local lvl = field(o, "BondLevel")
    if lvl ~= nil then return string.format("bond level %s progress %s", tostring(lvl), tostring(field(o, "BondProgress"))) end
    if type(o) == "userdata" then
        local ok, valid = pcall(function() return o:IsValid() end)
        if ok then return valid and who(o) or "(null object)" end
        local okn, n = pcall(function() return #o end)                  -- an array of objects: the first few names
        if okn and type(n) == "number" and n >= 0 then
            local names = {}
            for i = 1, math.min(n, 6) do names[#names + 1] = who(o[i]) end
            return "[" .. table.concat(names, ", ") .. (n > 6 and ", ..." or "") .. "]"
        end
    end
    return fmt(o)
end

-- one unit's numbers, or nil when it cannot be read right now
local function readUnit(c)
    local asc = c.AbilitySystemComponent
    if not (asc and asc:IsValid()) then return nil end
    local sets = asc.SpawnedAttributes
    if not sets then return nil end
    local u = {}
    for i = 1, sets:GetArrayNum() do
        local s = sets[i]
        local cn = s:GetClass():GetFName():ToString()
        if cn == "BitReactorHealthSet" then
            u.hp, u.maxhp = num(s.Health.CurrentValue), num(s.MaxHealth.CurrentValue)
        elseif cn == "BitReactorCombatSet" then
            u.armor, u.shields = num(s.CurrentArmor.CurrentValue), num(s.CurrentShields.CurrentValue)
            u.ap, u.mp = num(s.ActionPoints.CurrentValue), num(s.MovementActionPoints.CurrentValue)
        end
    end
    local l = c:K2_GetActorLocation()
    u.x, u.y, u.z = num(l.X), num(l.Y), num(l.Z)
    return u
end

-- one pass over the units in the level, once per round: this is the only place units are read in bulk
local function snapshot(reason)
    logLine("---- units (" .. reason .. ")")
    for _, c in ipairs(FindAllOf("Character") or {}) do
        if c:IsValid() and c:GetFullName():find("PersistentLevel.Char_", 1, true) then
            local u = readUnit(c)
            if u and (u.hp or 0) > 0 then
                logLine(string.format("%s  hp %s/%s armor %s shields %s ap %s move %s at (%d,%d)", c:GetFName():ToString(),
                    g(u.hp), g(u.maxhp), g(u.armor), g(u.shields), g(u.ap), g(u.mp), math.floor(u.x or 0), math.floor(u.y or 0)))
            end
        end
    end
end

local lastShot = {}
local CHARACTER = "/Game/Game/Core/Pawns/Char_BrunoBaseCharacter.Char_BrunoBaseCharacter_C:"
local GAMEPLAY = "/Script/BitReactorGame."

-- Hookable events: functions the game's native code (or a delegate) calls through Unreal's reflection. Blueprint functions that are called
-- inside their own graph (OnUnitDamaged on the character, DisplayDamageNumber, the accuracy calculation ...) are never seen by a hook.
local events = {
    -- every character that listens gets this call, so only the target's own copy is logged; a burst is one SHOT line per second
    { CHARACTER .. "OnProjectileFired", function(self, firer, target)
        if who(self) ~= who(target) then return end
        local line = string.format("SHOT    %s -> %s", who(firer), who(target))
        local now = os.time()
        if lastShot[line] ~= now then lastShot[line] = now; logLine(line) end
    end },
    { GAMEPLAY .. "UnitEventDelegateHandler:OnUnitDamaged", function(self, p)
        local o = payload(p)
        logLine(string.format("HIT     %s <- %s  damage %s%s  via %s", who(field(o, "Target")), who(field(o, "Instigator")), g(num(field(o, "DamageReceived"))),
            field(o, "bWasCriticalDamage") == true and " CRIT" or "", abilityOf(field(o, "InstigatorAbility"))))
    end },
    { GAMEPLAY .. "UnitEventDelegateHandler:OnUnitKilled", function(self, p)
        local o = payload(p)
        if field(o, "bIsLoadGameReapply") == true then return end              -- the game re-applies earlier deaths when a save loads
        logLine(string.format("KILLED  %s <- %s  damage %s  via %s", who(field(o, "Target")), who(field(o, "Instigator")), g(num(field(o, "DamageReceived"))),
            abilityOf(field(o, "InstigatorAbility"))))
    end },
    { GAMEPLAY .. "UnitEventDelegateHandler:OnUnitMoving", function(self, p)
        local o = payload(p)
        local loc = field(o, "MovementTargetLocation")
        logLine(string.format("MOVE    %s -> (%d,%d)", who(field(o, "Target")), math.floor(num(field(loc, "X")) or 0), math.floor(num(field(loc, "Y")) or 0)))
    end },
    { "/Script/Bruno.TSB_AbilityEventHandler:OnAbilityPresented", function(self, p)
        local name = abilityOf(p)
        if name:find("Listener") or name:find("^GA_Passive") or name:find("^GA_Cooldown") or name:find("DifficultTarget") then return end   -- passives are noise
        local owner = "?"
        pcall(function()
            local ab = obj(p)
            if ab then
                local a = ab:GetOwningActorFromActorInfo()
                if a and a:IsValid() then owner = a:GetFName():ToString() end
            end
        end)
        logLine(string.format("ABILITY %s  %s", owner, name))
    end },
    { GAMEPLAY .. "TurnEventDelegateHandler:OnTeamTurnStarted", function(self, p) logLine("== " .. who(p) .. " turn starts") end },
    { GAMEPLAY .. "TurnEventDelegateHandler:OnGameTurnStarted", function(self, n)
        local round = "?"
        pcall(function() round = tostring(n:get()) end)
        logLine("== round " .. round)
        snapshot("round " .. round)
    end },
    { GAMEPLAY .. "AbilityEventDelegateHandler:OnAbilityEnded", function(self, ability, cancelled)
        local c = false
        pcall(function() c = cancelled:get() == true end)
        local name = abilityOf(ability)
        -- only the abilities a player would care about being cancelled; passives and a dead unit's cleanup cancel all the time
        if c and (name:find("Shot") or name:find("Melee") or name:find("Move") or name:find("Grenade") or name:find("Overwatch")) then logLine("ABILITY cancelled  " .. name) end
    end },
    { GAMEPLAY .. "AbilityEventDelegateHandler:OnTargetingStarted", function(self, ability, target) logLine("AIM     " .. abilityOf(ability) .. " at " .. who(target)) end },
    { GAMEPLAY .. "AbilityEventDelegateHandler:OnSelectedTargetsChanged", function(self, targets)
        local text = describeParam(targets)
        if text ~= "?" and text ~= "[]" then logLine("AIM     targets " .. text) end      -- the array arrives unreadable at times
    end },
    { GAMEPLAY .. "PhaseEventDelegateHandler:OnSetGameSubPhase", function(self, old, new)
        local a, b = describeParam(old), describeParam(new)
        if (a .. b):find("AbilityBeat") then return end                     -- two of these per ability: not worth a line
        logLine("PHASE   " .. a:gsub("^tag ", "") .. " -> " .. b:gsub("^tag ", ""))
    end },
    { CHARACTER .. "ReceiveUnitDowned", function(self) logLine(string.format("DOWNED  %s", who(self))) end },
    { CHARACTER .. "ReceiveUnitHealed", function(self, source, ability) logLine(string.format("HEAL    %s <- %s  via %s", who(self), who(source), abilityOf(ability))) end },
}

-- Events that did not fire in an ordinary enemy phase (objectives, bonds, destroyed props, reinforcements, toasts ...): hooked all the same,
-- since a hook costs nothing until its event happens. Each one logs "EVENT  <what>  <its arguments>".
local function generic(label)
    return function(self, ...)
        local parts = {}
        for i = 1, select("#", ...) do parts[#parts + 1] = describeParam((select(i, ...))) end
        logLine("EVENT   " .. label .. (#parts > 0 and ("  " .. table.concat(parts, ", ")) or ""))
    end
end
for _, e in ipairs({
    { GAMEPLAY .. "ObjectiveEventDispatcher:BroadcastTacticalObjectiveActivatedEvent", "tactical objective activated" },
    { GAMEPLAY .. "ObjectiveEventDispatcher:BroadcastTacticalObjectiveProgressUpdatedEvent", "tactical objective progress" },
    { GAMEPLAY .. "ObjectiveEventDispatcher:BroadcastTacticalObjectiveSucceededEvent", "tactical objective succeeded" },
    { GAMEPLAY .. "ObjectiveEventDispatcher:BroadcastTacticalObjectiveFailedEvent", "tactical objective failed" },
    { GAMEPLAY .. "ObjectiveEventDispatcher:BroadcastMissionObjectiveActivatedEvent", "mission objective activated" },
    { GAMEPLAY .. "ObjectiveEventDispatcher:BroadcastMissionObjectiveProgressUpdatedEvent", "mission objective progress" },
    { GAMEPLAY .. "ObjectiveEventDispatcher:BroadcastMissionObjectiveSucceededEvent", "mission objective succeeded" },
    { GAMEPLAY .. "ObjectiveEventDispatcher:BroadcastMissionObjectiveFailedEvent", "mission objective failed" },
    { GAMEPLAY .. "BondEventDelegateHandler:OnBondProgressed", "bond progressed" },
    { GAMEPLAY .. "BondEventDelegateHandler:OnBondCreated", "bond created" },
    { GAMEPLAY .. "DestructionEventDispatcher:BroadcastOnAnyDestructionEventOccurred", "prop destroyed" },
    { GAMEPLAY .. "UIEventDispatcher:BroadcastReinforcementEvent", "reinforcements" },
    { GAMEPLAY .. "UIEventDispatcher:BroadcastToastEvent", "toast" },
    { GAMEPLAY .. "UnitEventDispatcher:BroadcastOnUnitVisualizedFatalDamage", "fatal damage shown" },
    { "/Script/Bruno.ETSG_TacticalEncounterEventHandler:OnTacticalCombatStarted", "combat started" },
    { "/Script/Bruno.ETSG_TacticalEncounterEventHandler:OnTacticalCombatEnded", "combat ended" },
}) do events[#events + 1] = { e[1], generic(e[2]) } end

local hooksState = nil          -- nil: not tried yet, otherwise "n of m event hooks installed"

local function installHooks()
    if hooksState then return hooksState end
    local ok = 0
    for _, e in ipairs(events) do
        local path, handler = e[1], e[2]
        local installed, err = trackedHook(path, function(...)
            if not logging or not alive() then return end
            local good, msg = pcall(handler, ...)
            if not good then logLine("hook error in " .. path:match("[^:]+$") .. ": " .. tostring(msg)) end
        end)
        if installed then ok = ok + 1 else logLine("could not hook " .. path:match("[^:]+$") .. ": " .. tostring(err)) end
    end
    hooksState = ok .. " of " .. #events .. " event hooks installed"
    return hooksState
end
if logging then installHooks() end        -- after a reload the new generation hooks again

function commands.log(args)
    local what = args:match("^%s*(%S*)")
    if what == "on" then
        local hooks = installHooks()
        logging = true; _G.ZC_LOGGING = true
        logLine("---- combat log started (" .. hooks .. ")")
        return "combat log on (" .. hooks .. "); lines go to " .. COMBAT_LOG
    elseif what == "off" then
        logging = false; _G.ZC_LOGGING = false
        logLine("---- combat log stopped")
        return "combat log off"
    elseif what == "clear" then
        os.remove(COMBAT_LOG)
        return "combat log cleared"
    elseif what == "snap" then
        snapshot("requested")
        return "snapshot written"
    end
    return "combat log is " .. (logging and "on" or "off") .. " (log on | log off | log clear | log snap)"
end

-- ---- investigating: funcs, call, trace, reload ------------------------------------------------
-- funcs <object|Class> [filter]: the functions a live object's class has (and its parents'), with parameter types.
-- call <object|Class> <Function> [args]: call one (numbers, true/false, @/object/path, n:Name, anything else is text). Changes the game: --write.
-- trace <Class:Function | /full/path:Function> [on|off|list] [max]: log each call of a function, with its parameters, to combat.log.
-- reload [file]: run this script again from disk (or from <file>) without restarting the mod.
local function isClassObject(o)
    local ok, full = pcall(function() return o:GetFullName() end)
    return ok and full and (full:find("^Class ") or full:find("^BlueprintGeneratedClass ")) ~= nil
end

local function paramList(fn)
    local parts = {}
    pcall(function()
        fn:ForEachProperty(function(p)
            local name = p:GetFName():ToString()
            -- a Blueprint function's own temporaries (CallFunc_..., K2Node_...) are properties of it too; they are not parameters
            if name:find("^CallFunc_") or name:find("^K2Node_") or name:find("^Temp_") or name:find("^NewLocalVar") then return end
            local t = "?"
            pcall(function() t = (p:GetClass():GetFName():ToString():gsub("Property$", "")) end)
            parts[#parts + 1] = t .. " " .. name
        end)
    end)
    return table.concat(parts, ", ")
end

function commands.funcs(args)
    local a = splitArgs(args, 2)
    local o, err = resolve(a[1])
    if not o then error(err) end
    local flt = (a[2] or ""):lower()
    local out, seen, depth = {}, {}, 0
    local cls = isClassObject(o) and o or o:GetClass()
    while cls and cls:IsValid() and depth < 14 and #out < 200 do
        depth = depth + 1
        local cn = cls:GetFName():ToString()
        pcall(function()
            cls:ForEachFunction(function(fn)
                local nm = fn:GetFName():ToString()
                if not seen[nm] and #out < 200 and (flt == "" or nm:lower():find(flt, 1, true)) then
                    seen[nm] = true
                    out[#out + 1] = cn .. ":" .. nm .. "(" .. paramList(fn) .. ")"
                end
            end)
        end)
        local ok, s = pcall(function() return cls:GetSuperStruct() end)
        if ok and s and s:IsValid() then cls = s else cls = nil end
    end
    return #out .. " function(s)" .. (#out > 0 and ("\n" .. table.concat(out, "\n")) or "")
end

local function parseArg(tok)
    if tok == "true" then return true elseif tok == "false" then return false end
    local n = tonumber(tok)
    if n then return n end
    local path = tok:match("^@(.+)$")
    if path then
        local o = StaticFindObject(path)
        if not (o and o:IsValid()) then error("no object at " .. path) end
        return o
    end
    local nm = tok:match("^n:(.+)$")
    if nm then return FName(nm) end
    return tok
end

function commands.call(args)
    local a = splitArgs(args, 3)
    local o, err = resolve(a[1])
    if not o then error(err) end
    local name = a[2]
    if not name or name == "" then error("usage: call <object|Class> <Function> [args]") end
    local params = {}
    for tok in (a[3] or ""):gmatch("%S+") do params[#params + 1] = parseArg(tok) end
    local res = table.pack(o[name](o, table.unpack(params, 1, #params)))
    local out = {}
    for i = 1, res.n do out[i] = fmt(res[i]) end
    return #out == 0 and "(called; no result)" or table.concat(out, "\t")
end

local traces = {}     -- function path -> { ids, count, limit }

local function functionPath(spec)
    if spec:find("^/") then return spec end
    local cls, fn = spec:match("^([^:]+):(.+)$")
    if not cls then error("usage: trace <Class:Function | /full/path:Function> [on|off|list] [max]") end
    local o = FindFirstOf(cls)
    if not (o and o:IsValid()) then error("no live instance of class " .. cls) end
    local c, depth = o:GetClass(), 0
    while c and c:IsValid() and depth < 14 do
        depth = depth + 1
        local full = c:GetFullName():match("^%S+%s+(.+)$")
        if full then
            local f = StaticFindObject(full .. ":" .. fn)
            if f and f:IsValid() then return full .. ":" .. fn end
        end
        local ok, s = pcall(function() return c:GetSuperStruct() end)
        if ok and s and s:IsValid() then c = s else c = nil end
    end
    error("no function " .. fn .. " on " .. cls .. " or its parents")
end

function commands.trace(args)
    local a = splitArgs(args, 3)
    local spec = a[1] or ""
    if spec == "" or spec == "list" then
        local lines = {}
        for path, t in pairs(traces) do lines[#lines + 1] = path .. "  (" .. t.count .. " calls)" end
        return #lines == 0 and "no traces" or table.concat(lines, "\n")
    end
    local path = functionPath(spec)
    local mode = (a[2] or "on"):match("^(%S+)") or "on"
    if mode == "off" then
        local t = traces[path]
        if not t then return "not tracing " .. path end
        pcall(UnregisterHook, path, t.pre, t.post)
        traces[path] = nil
        return "stopped tracing " .. path .. " after " .. t.count .. " calls"
    end
    if traces[path] then return "already tracing " .. path end
    local limit = tonumber((a[3] or ""):match("%d+")) or 300
    local short = path:match("[^:]+$")
    local t = { count = 0 }
    -- logged from the post-hook, so the line also carries the function's result (the last value, when the function returns one)
    local ok, pre, post = trackedHook(path, function() end, function(self, ...)
        if not alive() then return end
        t.count = t.count + 1
        if t.count > limit then return end
        local parts = { "self=" .. who(self) }
        for i = 1, select("#", ...) do
            local p = select(i, ...)
            local good, v = pcall(describeParam, p)
            parts[#parts + 1] = "p" .. i .. "=" .. (good and v or "?")
        end
        logLine("CALL    " .. short .. "(" .. table.concat(parts, ", ") .. ")")
        if t.count == limit then logLine("CALL    " .. short .. ": " .. limit .. " lines written; further calls are only counted (trace ... off to stop)") end
    end)
    if not ok then error("could not hook " .. path .. ": " .. tostring(pre)) end
    t.pre, t.post = pre, post
    traces[path] = t
    return "tracing " .. path .. "; the first " .. limit .. " calls go to " .. COMBAT_LOG
end

function commands.reload(args)
    local path = args:match("^%s*(.-)%s*$")
    if path == "" then path = SCRIPT end
    if not path then error("do not know where this script lives; pass a file") end
    local f = io.open(path, "rb")
    if not f then error("cannot read " .. path) end
    local src = f:read("*a")
    f:close()
    local chunk, err = load(src, "@" .. path, "t", _ENV or _G)
    if not chunk then error("not reloaded, syntax error: " .. tostring(err)) end          -- the running version stays
    local newVersion = src:match('local VERSION = "([^"]+)"') or "?"
    local oldGen = GEN
    -- run it after this request has been answered
    ExecuteWithDelay(300, function()
        ExecuteInGameThread(function()
            local ok, e = pcall(chunk)
            if not ok then
                _G.ZC_GEN = oldGen          -- the new generation failed half way: the running one carries on
                print("[ZcDebug] reload failed: " .. tostring(e) .. "\n")
            end
        end)
    end)
    return "reloading " .. VERSION .. " -> " .. newVersion
end

-- ---- loop --------------------------------------------------------------------------------
local function handle(id, command, args)
    local fn = commands[command]
    if not fn then reply(id, false, "unknown command '" .. command .. "'"); return end
    local ok, result = pcall(fn, args)
    if ok then reply(id, true, result == nil and "" or tostring(result)) else reply(id, false, tostring(result)) end
end

local lastBeat = 0
-- what the request loop last did, written into the heartbeat so a stuck channel can be diagnosed without the log
local stage, handled = "start", 0
local function tick()
    local now = os.time()
    if now ~= lastBeat then
        lastBeat = now
        writeAtomic(HEARTBEAT, now .. "\t" .. esc("zcdebug-mod " .. VERSION) .. "\t" .. esc(string.format("busy=%s last=%s handled=%d stage=%s",
            tostring(busy), tostring(lastId), handled, stage)) .. "\n")
    end
    if busy then
        -- a callback the game thread never ran (handed over while the engine was still starting, say) must not deafen the mod for good
        if now - busySince < 30 then return false end
        print("[ZcDebug] a request was not run by the game thread within 30s; taking requests again\n")
        busy = false
    end
    local text = readAll(REQUEST)
    if not text then stage = "no request file"; return false end
    if text:sub(-1) ~= "\n" then stage = "request incomplete"; return false end
    local id, command, args = text:gsub("[\r\n]+$", ""):match("^([^\t]*)\t([^\t]*)\t?(.*)$")
    if not id or id == "" then stage = "request unreadable"; return false end
    if id == lastId then stage = "idle"; return false end
    lastId = id
    busy = true
    busySince = now
    stage = "queued " .. command
    ExecuteInGameThread(function()          -- reflection must run on the game thread
        stage = "running " .. command
        handle(id, command, unesc(args or ""))
        handled = handled + 1
        stage = "done " .. command
        busy = false
    end)
    return false
end

-- a request left in the file by an earlier run (or an earlier generation) is not run again when this script starts
do
    local text = readAll(REQUEST)
    if text then lastId = text:match("^([^\t\r\n]*)") end
end

LoopAsync(200, function()
    if not alive() then return true end
    local ok, err = pcall(tick)
    if not ok then print("[ZcDebug] " .. tostring(err) .. "\n"); busy = false end
    return false
end)

print("[ZcDebug] " .. VERSION .. " ready; channel " .. DIR .. "\n")
