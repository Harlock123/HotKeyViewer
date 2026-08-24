-- Recovers keybindings from a Lua-based Hyprland config by *executing* it
-- under a stub environment rather than parsing it.
--
-- Hyprland's Lua config provider reports every Lua-defined bind as dispatcher
-- "__lua" with an opaque numeric arg, so `hyprctl binds` alone can say which
-- chord is bound but never what it actually runs. Bindings are also produced by
-- loops and conditionals (`for workspace = 1, 10`, `if o.preinstalled_bindings_enabled()`),
-- so a static text parse would miss or invent entries. Running the real config
-- against a fake `hl` is the only way to see exactly the set Hyprland saw.
--
-- Emits one TSV record per line on stdout:
--   bind    <modmask> <key> <description> <kind> <arg> <file> <line>
--   unbind  <modmask> <key> <> <> <> <file> <line>
-- Diagnostics go to stderr so they can never corrupt the records.

local ESCAPES = { ["\\"] = "\\\\", ["\t"] = "\\t", ["\n"] = "\\n", ["\r"] = "\\r" }

local function escape(value)
  return (tostring(value or ""):gsub("[\\\t\n\r]", ESCAPES))
end

local function emit(...)
  local fields = {}
  for index = 1, select("#", ...) do
    fields[index] = escape((select(index, ...)))
  end
  io.stdout:write(table.concat(fields, "\t"), "\n")
end

-- Mirrors Hyprland's modifier bitmask so records can be matched against
-- `hyprctl binds` output, which only reports the numeric mask.
local MODIFIERS = {
  SHIFT = 1, CAPS = 2, CAPSLOCK = 2,
  CTRL = 4, CONTROL = 4,
  ALT = 8, MOD1 = 8, MOD2 = 16, MOD3 = 32,
  SUPER = 64, SUPER_L = 64, SUPER_R = 64, MOD4 = 64, WIN = 64, LOGO = 64,
  MOD5 = 128,
}

local function split_keys(keys)
  local modmask, key = 0, ""

  for part in tostring(keys or ""):gmatch("[^+]+") do
    local value = part:gsub("^%s+", ""):gsub("%s+$", "")
    local modifier = MODIFIERS[value:upper()]

    if modifier then
      -- Bitwise-or, not addition: a chord may repeat a modifier without
      -- doubling its bit.
      modmask = modmask | modifier
    elseif value ~= "" then
      key = value
    end
  end

  return modmask, key
end

-- The file that defined a bind is the first stack frame outside this script and
-- outside Omarchy's helpers, which wrap every `o.bind` call. Walking the stack
-- is what separates "you set this" from "the distro set this".
local SELF_SOURCE = debug.getinfo(1, "S").short_src

local function definition_site()
  for level = 2, 24 do
    local info = debug.getinfo(level, "S")
    if not info then break end

    local source = info.short_src or ""
    local is_wrapper = source:find("helpers%.lua$") or source:find("omarchy%.lua$")
      or source:find("require_all%.lua$") or source:find("require_optional%.lua$")

    if source ~= SELF_SOURCE and source ~= "[C]" and not is_wrapper then
      local line = debug.getinfo(level, "l")
      return source, (line and line.currentline) or 0
    end
  end

  return "", 0
end

-- Rebuilds the Lua source text of a dispatcher call so the UI can show
-- something meaningful for binds that don't shell out to a command.
local function literal(value)
  local kind = type(value)

  if kind == "string" then
    return string.format("%q", value)
  elseif kind == "number" or kind == "boolean" then
    return tostring(value)
  elseif kind == "table" then
    local parts, named, length = {}, {}, #value

    for index = 1, length do
      parts[#parts + 1] = literal(value[index])
    end

    for key in pairs(value) do
      local is_array_index = type(key) == "number" and key >= 1 and key <= length and math.floor(key) == key
      if not is_array_index then named[#named + 1] = key end
    end

    table.sort(named, function(left, right) return tostring(left) < tostring(right) end)

    for _, key in ipairs(named) do
      local prefix = (type(key) == "string" and key:match("^[%a_][%w_]*$"))
        and (key .. " = ")
        or ("[" .. literal(key) .. "] = ")
      parts[#parts + 1] = prefix .. literal(value[key])
    end

    return "{ " .. table.concat(parts, ", ") .. " }"
  end

  return "nil"
end

local function call_expression(path, ...)
  local args = {}
  for index = 1, select("#", ...) do
    args[index] = literal((select(index, ...)))
  end
  return path .. "(" .. table.concat(args, ", ") .. ")"
end

local function dispatcher_record(kind, arg, expression)
  return { __scanned = true, kind = kind or "", arg = arg or "", expression = expression or "" }
end

-- `hl.dsp.window.close()` and friends are arbitrary dotted paths, so proxy any
-- attribute access and record the call text when it is finally invoked.
local function dsp_proxy(path)
  return setmetatable({ path = path }, {
    __index = function(self, key) return dsp_proxy(self.path .. "." .. tostring(key)) end,
    __call = function(self, ...)
      local expression = call_expression(self.path, ...)
      local first = ...

      if self.path == "hl.dsp.exec_cmd" and type(first) == "string" then
        return dispatcher_record("exec", first, expression)
      end

      return dispatcher_record("lua", expression, expression)
    end,
  })
end

-- Absorbs every call the real config makes into Hyprland that we don't model,
-- so loading never aborts partway and silently truncates the bind list.
local noop
noop = setmetatable({}, {
  __index = function() return noop end,
  __call = function() return noop end,
  __tostring = function() return "" end,
  __concat = function() return "" end,
})

local function describe_dispatcher(value)
  if type(value) == "table" and value.__scanned then
    return value.kind or "", (value.arg ~= "" and value.arg) or value.expression or ""
  elseif type(value) == "string" and value ~= "" then
    return "exec", value
  elseif type(value) == "function" then
    return "lua", "<lua function>"
  end
  return "", ""
end

hl = setmetatable({
  dsp = dsp_proxy("hl.dsp"),

  bind = function(keys, dispatcher, options)
    options = (type(options) == "table") and options or {}

    local modmask, key = split_keys(keys)
    local kind, arg = describe_dispatcher(dispatcher)
    local file, line = definition_site()

    emit("bind", modmask, key, options.description or "", kind, arg, file, line)
    return noop
  end,

  unbind = function(keys)
    local modmask, key = split_keys(keys)
    local file, line = definition_site()

    emit("unbind", modmask, key, "", "", "", file, line)
    return noop
  end,

  -- Omarchy's helpers branch on this; nil keeps them on their default path.
  get_config = function() return nil end,
  get_active_window = function() return nil end,
}, {
  __index = function() return noop end,
})

local config_path = ...

if not config_path or config_path == "" then
  io.stderr:write("usage: scan-lua-config.lua <path-to-hyprland.lua>\n")
  os.exit(2)
end

local ok, err = pcall(dofile, config_path)

if not ok then
  -- Partial results are still worth returning: a failure late in the config
  -- leaves every bind emitted before it valid.
  io.stderr:write("lua config scan failed: " .. tostring(err) .. "\n")
  os.exit(1)
end
