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

local VERSION = "0.1"
local DIR = (os.getenv("LOCALAPPDATA") or ".") .. "\\ZeroCompanyEditor\\debug\\"
local REQUEST, RESPONSE, HEARTBEAT = DIR .. "request.txt", DIR .. "response.txt", DIR .. "heartbeat.txt"

local lastId = nil
local busy = false

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
    -- userdata: an object, a name, a string or an array
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

function commands.get(args)
    local a = splitArgs(args, 2)
    local o, err = resolve(a[1])
    if not o then error(err) end
    -- a path like SpawnedAttributes.1.Health: numbers index arrays (1-based), names read properties
    local v = o
    for part in a[2]:gmatch("[^%.]+") do
        local n = tonumber(part)
        local ok, nxt = pcall(function() if n then return v[n] end return v[part] end)
        if not ok then error("cannot read '" .. part .. "': " .. tostring(nxt)) end
        v = nxt
        if v == nil then error("'" .. part .. "' is nil") end
    end
    -- an array with no index: list its elements
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
    while c and c:IsValid() do
        local owner = c:GetFName():ToString()
        c:ForEachProperty(function(p)
            local name = p:GetFName():ToString()
            local ty = "?"
            pcall(function() ty = p:GetClass():GetFName():ToString() end)
            if ty:find("Delegate") then return end
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

-- ---- loop --------------------------------------------------------------------------------
local function handle(id, command, args)
    local fn = commands[command]
    if not fn then reply(id, false, "unknown command '" .. command .. "'"); return end
    local ok, result = pcall(fn, args)
    if ok then reply(id, true, result == nil and "" or tostring(result)) else reply(id, false, tostring(result)) end
end

local lastBeat = 0
local function tick()
    local now = os.time()
    if now ~= lastBeat then
        lastBeat = now
        writeAtomic(HEARTBEAT, now .. "\t" .. esc("zcdebug-mod " .. VERSION) .. "\n")
    end
    if busy then return false end
    local text = readAll(REQUEST)
    if not text or text:sub(-1) ~= "\n" then return false end
    local id, command, args = text:gsub("[\r\n]+$", ""):match("^([^\t]*)\t([^\t]*)\t?(.*)$")
    if not id or id == "" or id == lastId then return false end
    lastId = id
    busy = true
    ExecuteInGameThread(function()          -- reflection must run on the game thread
        handle(id, command, unesc(args or ""))
        busy = false
    end)
    return false
end

LoopAsync(200, function()
    local ok, err = pcall(tick)
    if not ok then print("[ZcDebug] " .. tostring(err) .. "\n"); busy = false end
    return false
end)

print("[ZcDebug] " .. VERSION .. " ready; channel " .. DIR .. "\n")
