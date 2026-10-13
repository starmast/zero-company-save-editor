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
    o:GetClass():ForEachProperty(function(p)
        local ty = "?"
        pcall(function() ty = p:GetClass():GetFName():ToString() end)
        out[#out + 1] = p:GetFName():ToString() .. " : " .. ty
    end)
    return table.concat(out, "\n")
end

function commands.get(args)
    local a = splitArgs(args, 2)
    local o, err = resolve(a[1])
    if not o then error(err) end
    return fmt(o[a[2]])
end

function commands.set(args)
    local a = splitArgs(args, 3)
    local o, err = resolve(a[1])
    if not o then error(err) end
    o[a[2]] = coerce(a[3])
    return "set " .. a[2] .. " = " .. fmt(o[a[2]])
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
