-- Standalone probes for the industry adapter. Each probe reproduces one
-- defect that the chain completeness fix must handle, using the documented
-- API shapes. The file is independent of scripts/industry-lua-check.lua so it
-- can also be run against the pre-fix adapter to prove a probe really catches
-- a regression.
--
--   lua scripts/industry-lua-probes.lua src/功能接口v6.lua
--   lua scripts/industry-lua-probes.lua scripts/fixtures/industry-baseline-lua6.lua

local INTERFACE = arg and arg[1] or "src/功能接口v6.lua"

local passed, failed = 0, 0
local function expect(value, message) if not value then error(message, 2) end end

local function load(opts)
    opts = opts or {}
    local CT = {
        INDUSTRY = "industry", CONSTRUCTION = "construction", STOCK_LIST = "stock_list", TOWN = "town",
        ACCOUNT = "account", GAME_TIME = "game_time", GAME_SPEED = "game_speed", WORLD = "world", NAME = "name",
        WAREHOUSE = "warehouse", TRANSPORT_VEHICLE = "vehicle", PLAYER_OWNED = "owned"
    }
    local components, names = {}, {}
    local function add(entity, componentType, value)
        components[entity] = components[entity] or {}
        components[entity][componentType] = value
    end
    local state = { world = 2, player = 1 }
    add(1, CT.ACCOUNT, { balance = 1000, loan = 0 }); names[1] = "Player"
    add(2, CT.GAME_TIME, { gameTime = 1, timeOfDaySec = 0 })
    add(2, CT.GAME_SPEED, { millisPerDay = 1000, speedup = 1 })
    add(2, CT.WORLD, { cloudCoverage = 0.5 }); names[2] = "World"

    local cargoCount = 8
    local cargoNames = {}
    for i = 1, cargoCount do cargoNames[i] = "Cargo" .. i end
    local catalogue = { [0] = "Passenger" }
    for i = 1, cargoCount do catalogue[i] = cargoNames[i] end

    local function matrix(x, y)
        return { [1] = 1, [2] = 0, [3] = 0, [4] = 0, [5] = 0, [6] = 1, [7] = 0, [8] = 0,
            [9] = 0, [10] = 0, [11] = 1, [12] = 0, [13] = x, [14] = y, [15] = 0, [16] = 1 }
    end

    local recipes, rules = {}, {}
    local count = opts.count or 1
    for i = 1, count do
        local entity, stockList, construction = 1000 + i, 5000 + i, 9000 + i
        add(entity, CT.INDUSTRY, { stockList = stockList, construction = construction, level = 1, maxLevel = 3 })
        add(stockList, CT.STOCK_LIST, { rules = {}, modifiers = { productivity = 1 }, stocks = {} })
        add(construction, CT.CONSTRUCTION, { fileName = "factory", transf = matrix(i * 10, i * 10) })
        names[entity] = "Industry " .. i
        recipes[stockList] = { inputs = { 1 }, outputs = { 2 } }
    end
    if opts.customRules then
        for stockList, value in pairs(opts.customRules) do
            components[stockList][CT.STOCK_LIST].rules = value
        end
    end
    if opts.customRecipes then
        for stockList, value in pairs(opts.customRecipes) do recipes[stockList] = value end
    end

    local townCount = opts.towns or 1
    for i = 1, townCount do
        local entity = 2000 + i
        add(entity, CT.TOWN, { initialLandUseCapacities = { 500, 200, 200 }, cargoNeeds = { {}, {}, {} } })
        names[entity] = "Town " .. i
    end

    local function recipeFunction(stockList)
        local recipe = recipes[stockList]
        if recipe == false then error("simulated recipe read failure") end
        if not recipe then return {}, {} end
        return recipe.inputs, recipe.outputs
    end
    local function centerFunction(entity, landUse)
        local n = type(landUse) == "number" and landUse or 0
        return { x = entity * 10 + n, y = n * 100 }
    end

    local function stub() end
    return {
        state = state,
        type = {
            ComponentType = CT,
            StockList = { Modifiers = { new = function() return {} end } },
            TransportVehicle = { Modifiers = { new = function() return {} end } },
            Vec5f = { new = function(...) return { ... } end },
            Date = { new = function(...) return { ... } end },
            Mat4f = {},
            enum = {
                LandUseType = {
                    RESIDENTIAL = { name = "residential" },
                    COMMERCIAL = { name = "commercial" },
                    INDUSTRIAL = { name = "industrial" }
                }
            }
        },
        engine = {
            entityExists = function(entity) return components[entity] ~= nil end,
            getComponent = function(entity, componentType) return components[entity] and components[entity][componentType] or nil end,
            getEntitiesWithComponent = function(componentType)
                local result = {}
                for entity, component in pairs(components) do
                    if component[componentType] ~= nil and entity > 2 then result[#result + 1] = entity end
                end
                return result
            end,
            getRevision = function() return 1 end,
            util = {
                getPlayer = function() return state.player end,
                getWorld = function() return state.world end,
                getEntityName = function(entity) return names[entity] end,
                getEntityNameStem = function(entity) return names[entity] end,
                getCalendarDate = function() return { year = 2026, month = 10, day = 30 } end,
                stock = { getInputsOutputsFromRules = recipeFunction, getStockCargoTypes = function() return {} end },
                town = {
                    getLandUse2CargoTypes = function() return {} end,
                    getTownDistrictCenter = centerFunction,
                    getTownStockCargo = function() return {} end
                },
                finance = { calcIncomeSince = function() return 0 end }
            },
            system = {
                simEntityAtStockSystem = { getStockCount = function() return 0 end, getStockCountForCargoType = function() return 0 end },
                gameScriptSystem = { getEntityForGameScript = function() return nil end }
            }
        },
        res = {
            cargoTypeRep = {
                getAll = function() return catalogue end,
                get = function(i) return { name = cargoNames[i] or catalogue[i], timeToDeliverInSeconds = 1 } end,
                getName = function(i) return cargoNames[i] or catalogue[i] end
            },
            constructionRep = {
                find = function() return 1 end,
                get = function() return { description = { name = "Factory" } } end
            }
        },
        cmd = { sendCommand = stub },
        gui = { camera = { getCameraData = function() return { x = 0, y = 0, z = 500, w = 0, v = 1 } end, getCenter = function() return { x = 0, y = 0 } end, focusEntity = stub, setCameraData = stub } },
        util = { getApplicationTime = function() return 1 end }
    }
end

local function newSession(opts)
    __SZTF3F6 = nil
    __SZTF3Callable6 = {}
    api = load(opts)
    _ug_loadedModules = {}
    dofile(INTERFACE)
    local S = __SZTF3F6
    expect(S and S.protocol == 6, "interface did not expose protocol 6 state")
    S.player = 1; S.world = 2; S.nameScope = "probe"
    return S, api
end

local function fetch(S, part)
    local offset = 0
    while true do
        local result = S.dispatch({ op = "industry_catalog", request = part .. offset, offset = offset, limit = 100, part = part })
        local total = tonumber(result:match('"total":(%d+)'))
        local count = 0
        if part == "industry" then
            for _ in result:gmatch('"entityId":"%d+"') do count = count + 1 end
        elseif part == "town" then
            for _ in result:gmatch('"entityId":"%d+"') do count = count + 1 end
        else
            local slice = result:match('"cargo":%[(.-)%]') or ""
            for _ in slice:gmatch('"id":"%d+"') do count = count + 1 end
        end
        if offset + count >= total or count == 0 then break end
        offset = offset + count
    end
end

local function index(opts)
    local S = newSession(opts)
    fetch(S, "industry"); fetch(S, "town"); fetch(S, "cargo")
    return S.industryIndex, S, api
end

local function ids(list, key)
    local out = {}
    for _, row in ipairs(list) do out[#out + 1] = row[key] end
    table.sort(out)
    return table.concat(out, ",")
end

local function probe(name, fn)
    local ok, why = pcall(fn)
    if ok then passed = passed + 1; print("PASS " .. name)
    else failed = failed + 1; print("FAIL " .. name .. " :: " .. tostring(why)) end
end

-- P1: cargoNeeds is three districts, each a list of {cargoTypeId, amount}.
probe("documented town pair list preserves cargo and amount", function()
    local S = newSession({ count = 1 })
    api.engine.getComponent(2001, api.type.ComponentType.TOWN).cargoNeeds = { { { 7, 100 } }, {}, {} }
    fetch(S, "industry"); fetch(S, "town"); fetch(S, "cargo")
    local t = S.industryIndex.towns[1]
    local detail = {}
    for _, n in ipairs(t.needs) do detail[#detail + 1] = n.cargoId .. ":" .. n.status end
    expect(#t.needs == 1 and t.needs[1].cargoId == "7" and t.needs[1].status == "active",
        table.concat(detail, ",") .. "; needsStatus=" .. t.needsStatus)
end)

-- P2: a zero demand must stay zero and must not invent a passenger demand.
probe("documented zero demand does not invent a passenger demand", function()
    local S = newSession({ count = 1 })
    api.engine.getComponent(2001, api.type.ComponentType.TOWN).cargoNeeds = { { { 7, 0 } }, {}, {} }
    fetch(S, "industry"); fetch(S, "town"); fetch(S, "cargo")
    local t = S.industryIndex.towns[1]
    expect(#t.needs == 1 and t.needs[1].cargoId == "7" and t.needs[1].status == "zero",
        "cargoIds=" .. ids(t.needs, "cargoId") .. "; needsStatus=" .. t.needsStatus)
end)

-- P3: a confirmed empty direction is a successful read, not an unread recipe.
probe("confirmed empty direction does not make the recipe unread", function()
    local idx = index({
        count = 1,
        customRules = { [5001] = { [1] = { input = {}, output = { [2] = 1 } } } },
        customRecipes = { [5001] = { inputs = {}, outputs = { 2 } } }
    })
    local e = idx.industries[1]
    expect(idx.complete and idx.unreadIndustryCount == 0,
        "input=" .. e.inputStatus .. ", output=" .. e.outputStatus .. ", recipe=" .. e.recipeStatus ..
        ", unread=" .. idx.unreadIndustryCount .. ", complete=" .. tostring(idx.complete))
end)

-- P4: a zero-based sparse official list must not turn offsets into cargo ids.
probe("zero-based sparse official list keeps the cargo ids", function()
    local idx = index({
        count = 1,
        customRules = { [5001] = {} },
        customRecipes = { [5001] = { inputs = { [0] = 7, [2] = 8 }, outputs = {} } }
    })
    local e = idx.industries[1]
    expect(ids(e.inputs, "cargoId") == "7,8", "actual cargoIds=" .. ids(e.inputs, "cargoId"))
end)

-- P5: an entry that could not be explained must not yield a verified status.
probe("unexplained official entry cannot yield a verified status", function()
    local idx = index({
        count = 1,
        customRules = { [5001] = {} },
        customRecipes = { [5001] = { inputs = { 7, "unreadable" }, outputs = {} } }
    })
    local e = idx.industries[1]
    expect(e.inputStatus == "partial", "inputStatus=" .. e.inputStatus .. ", unparsed=" .. e.unparsedRuleCount)
end)

-- P6: an unexplained rule group must not yield a verified status either.
probe("unexplained rule group cannot yield a verified status", function()
    local idx = index({
        count = 1,
        customRules = { [5001] = { [1] = { input = { { 7 }, unreadable = "invalid" }, output = { [2] = 1 } } } },
        customRecipes = { [5001] = { inputs = { 7 }, outputs = { 2 } } }
    })
    local e = idx.industries[1]
    expect(e.inputStatus == "partial", "inputStatus=" .. e.inputStatus .. ", unparsed=" .. e.unparsedRuleCount)
end)

-- P7: an explicit refresh must retry a failed cargo catalogue read.
probe("explicit refresh retries a failed cargo catalogue read", function()
    local S = newSession({ count = 1 })
    local getter = api.res.cargoTypeRep.getAll
    api.res.cargoTypeRep.getAll = function() error("temporary catalogue failure") end
    fetch(S, "industry"); fetch(S, "town"); fetch(S, "cargo")
    expect(not S.industryIndex.cargoCatalogueComplete, "catalogue failure not simulated")
    api.res.cargoTypeRep.getAll = getter
    S.dispatch({ op = "industry_refresh", request = "retry" })
    fetch(S, "industry"); fetch(S, "town"); fetch(S, "cargo")
    expect(S.industryIndex.cargoCatalogueComplete,
        "catalogueComplete=" .. tostring(S.industryIndex.cargoCatalogueComplete) ..
        "; cached complete=" .. tostring(S.cargoCatalogue and S.cargoCatalogue.complete))
end)

-- P8: a cargo that only exists inside a rule must still be a relation.
probe("cargo that only exists inside a rule is still a relation", function()
    local idx = index({
        count = 2,
        customRules = { [5001] = { [1] = { input = { { 9 } }, output = { [10] = 1 } } } },
        customRecipes = { [5001] = { inputs = {}, outputs = { 10 } }, [5002] = { inputs = {}, outputs = { 9 } } }
    })
    local mill
    for _, e in ipairs(idx.industries) do if e.entityId == "1001" then mill = e end end
    local found = false
    for _, id in ipairs(mill.inputIds) do if id == "9" then found = true end end
    expect(found, "mill inputIds=" .. ids(mill.inputs, "cargoId") .. " (the cargo only exists inside the rule)")
end)

-- P9: cargo id 0 is a real cargo id when the resource catalogue lists it.
-- A reader that only accepts positive ids loses the relation silently.
probe("catalogue listed cargo id 0 is kept as a relation", function()
    local idx = index({
        count = 1,
        customRules = { [5001] = { [1] = { input = { { 0 } }, output = { [2] = 1 } } } },
        customRecipes = { [5001] = { inputs = { 0 }, outputs = { 2 } } }
    })
    local e = idx.industries[1]
    expect(ids(e.inputs, "cargoId") == "0", "actual cargoIds=" .. ids(e.inputs, "cargoId"))
end)

-- P10: a rule may carry the cargo id in a record instead of a bare number.
-- Reading only bare numbers loses the relation silently.
probe("record shaped rule cargo reference is kept", function()
    local idx = index({
        count = 1,
        customRules = { [5001] = { [1] = { input = { { { cargoType = 7 } } }, output = { [2] = 1 } } } },
        customRecipes = { [5001] = { inputs = {}, outputs = { 2 } } }
    })
    local e = idx.industries[1]
    expect(ids(e.inputs, "cargoId") == "7", "actual cargoIds=" .. ids(e.inputs, "cargoId"))
end)

-- P11: the cargo catalogue is only sealed once every industry was read, so a
-- cargo that only appears in the rule of a later page is still listed.
probe("cargo of a later industry page is still in the catalogue", function()
    local S = newSession({
        count = 120,
        customRules = { [5120] = { [1] = { input = { { 21 } }, output = { [22] = 1 } } } },
        customRecipes = { [5120] = { inputs = { 21 }, outputs = { 22 } } }
    })
    fetch(S, "industry"); fetch(S, "town"); fetch(S, "cargo")
    local cargoIds = {}
    for _, cargo in ipairs(S.industryIndex.cargo) do cargoIds[cargo.id] = true end
    expect(cargoIds["21"] and cargoIds["22"],
        "21=" .. tostring(cargoIds["21"]) .. " 22=" .. tostring(cargoIds["22"]))
end)

print(string.format("INDUSTRY_LUA_PROBES: %d passed / %d failed", passed, failed))
if failed > 0 then os.exit(1) end
