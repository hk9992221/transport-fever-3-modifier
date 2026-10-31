-- Reproducible Lua-side regression check for the industry catalogue adapter.
-- Run from the repository root with Lua 5.4:
--   lua scripts/industry-lua-check.lua
-- or pass the interface path:
--   lua scripts/industry-lua-check.lua src/功能接口v6.lua

local INTERFACE = arg and arg[1] or "src/功能接口v6.lua"
local BUDGET = 480 * 1024

local function fail(message) error(message, 2) end
local function expect(value, message) if not value then fail(message) end end

local function makeMock(opts)
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

    local cargoCount = opts.big and 300 or 8
    local cargoNames = {}
    for i = 1, cargoCount do
        cargoNames[i] = opts.big and string.format("Cargo%03d-long-cargo-name", i) or ("Cargo" .. i)
    end
    -- The resource catalogue is the source of truth for cargo identity. Cargo
    -- id 0 is the passenger id and is present in every real catalogue, so it
    -- must survive the whole pipeline. opts.noPassengerCargo removes it to
    -- prove that an unlisted id 0 is not silently accepted.
    local catalogue = { [0] = "Passenger" }
    for i = 1, cargoCount do catalogue[i] = cargoNames[i] end
    if opts.noPassengerCargo then catalogue[0] = nil end
    local recipes, rules = {}, {}
    local industryEntities, townEntities = {}, {}
    local function matrix(x, y)
        return { [1] = 1, [2] = 0, [3] = 0, [4] = 0, [5] = 0, [6] = 1, [7] = 0, [8] = 0,
            [9] = 0, [10] = 0, [11] = 1, [12] = 0, [13] = x, [14] = y, [15] = 0, [16] = 1 }
    end
    local count = opts.count or 6
    for i = 1, count do
        local entity, stockList, construction = 1000 + i, 5000 + i, 9000 + i
        industryEntities[#industryEntities + 1] = entity
        add(entity, CT.INDUSTRY, { stockList = stockList, construction = construction, level = 1, maxLevel = 3, manualDevelopment = false })
        add(stockList, CT.STOCK_LIST, { rules = rules[stockList] or {}, modifiers = { productivity = 1 }, stocks = {} })
        add(construction, CT.CONSTRUCTION, { fileName = "factory", transf = matrix(i * 10, i * 10) })
        names[entity] = "Industry " .. i
        local inputs, outputs
        if opts.big then
            inputs, outputs = {}, {}
            for cargo = 1, 150 do inputs[#inputs + 1] = cargo end
            for cargo = 151, 300 do outputs[#outputs + 1] = cargo end
        else
            inputs, outputs = { 1 }, { 2 }
        end
        recipes[stockList] = { inputs = inputs, outputs = outputs }
        if opts.failAt == i then recipes[stockList] = false end
    end
    if not opts.big and count >= 1 then
        -- Engine.Component.StockList.rules is a map keyed by rule index.
        -- Rule.input is a list of alternative groups and Rule.output is a
        -- map from CargoTypeId to amount.
        rules[5001] = { [1] = { input = { { 1 } }, output = { [2] = 1 } },
                       [2] = { input = { { 4 } }, output = {}, booster = true } }
        components[5001][CT.STOCK_LIST].rules = rules[5001]
    end
    -- A hand written fixture lets a test control the exact rule shape of a
    -- single industry: opts.customRules[stockList] holds its rules and
    -- opts.customRecipes[stockList] holds its official input/output lists.
    if opts.customRules then
        for stockList, value in pairs(opts.customRules) do
            components[stockList][CT.STOCK_LIST].rules = value
            if not recipes[stockList] then recipes[stockList] = { inputs = {}, outputs = {} } end
        end
    end
    if opts.customRecipes then
        for stockList, value in pairs(opts.customRecipes) do recipes[stockList] = value end
    end

    local townCount = opts.towns or 2
    for i = 1, townCount do
        local entity = 2000 + i
        townEntities[#townEntities + 1] = entity
        -- Engine.Component.Town.cargoNeeds is documented as
        -- {{{CargoTypeId, number}}}: three districts, each a list of demand
        -- groups, each group a list of {cargo type id, amount} pairs.
        local needs = { { { { 2, 100 } } }, { { { 3, 0 } } }, { } }
        if i == 2 then needs = { { }, { { { 3, 50 } } }, { } } end
        if opts.unreadTown and i == townCount then needs = nil end
        add(entity, CT.TOWN, { initialLandUseCapacities = { 500, 200, 200 }, cargoNeeds = needs })
        names[entity] = "Town " .. i
    end

    local function recipeFunction(stockList)
        local recipe = recipes[stockList]
        if recipe == false then error("simulated recipe read failure") end
        if not recipe then return {}, {} end
        return recipe.inputs, recipe.outputs
    end
    -- The district centre lookup takes a single LandUseType value. The mock
    -- records which value it received so the test can prove that the enum
    -- member was used instead of the positional district number.
    local lastLandUse
    local function centerFunction(entity, landUse)
        lastLandUse = landUse
        return { x = entity * 10 + (type(landUse) == "number" and landUse or 0), y = (type(landUse) == "number" and landUse or 0) * 100 }
    end
    local function bind(fn)
        if opts.binding ~= "protected" then return fn end
        local callable = setmetatable({}, { __call = function(_, ...) return fn(...) end })
        if opts.proof then __SZTF3Callable6[callable] = true end
        return callable
    end

    local function stub() end
    return {
        state = state,
        lastLandUse = function() return lastLandUse end,
        type = {
            ComponentType = CT,
            StockList = { Modifiers = { new = function() return {} end } },
            TransportVehicle = { Modifiers = { new = function() return {} end } },
            Vec5f = { new = function(...) return { ... } end },
            Date = { new = function(...) return { ... } end },
            Mat4f = {},
            -- api.type.enum.LandUseType holds the district singletons. The
            -- numeric value of a district is not published, so the members
            -- are values, not numbers.
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
                stock = {
                    getInputsOutputsFromRules = bind(recipeFunction),
                    getStockCargoTypes = function() return {} end
                },
                town = {
                    getLandUse2CargoTypes = function() return { [1] = { 2, 3 }, [2] = { 4 }, [3] = { 5 } } end,
                    getTownDistrictCenter = bind(centerFunction),
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
        cmd = {
            sendCommand = stub, makeJournalBookAssetCmd = stub, makeStockListSetModifiersCmd = stub,
            makeIndustrySetManualDevelopmentCmd = stub, makeVehicleSetModifiersCmd = stub,
            makeGameSetCalendarSpeedCmd = stub, makeGameSetSpeedCmd = stub, makeGameSetDateCmd = stub,
            makeGameSetTimeOfDayCmd = stub, makeGameSetCloudCoverageCmd = stub, makeScriptingSendEventCmd = stub,
            makeEntitySetNameCmd = stub, makeTownSetInitialLandUseCapacitiesCmd = stub,
            debug = { makeStockSetCargoAmountCmd = stub }
        },
        gui = {
            camera = {
                getCameraData = function() return { x = 0, y = 0, z = 500, w = 0, v = 1 } end,
                getCenter = function() return { x = 0, y = 0 } end,
                focusEntity = stub, setCameraData = stub
            }
        },
        util = { getApplicationTime = function() return 1 end }
    }
end

local function makeNativeVector()
    -- Lua 5.4 returns a file userdata; fengari lacks io.tmpfile, so a thread
    -- provides the same non-table field-access path for local runs.
    if io.tmpfile then return io.tmpfile(), true end
    return coroutine.create(function() end), false
end

local function load(opts)
    __SZTF3F6 = nil
    __SZTF3Callable6 = {}
    api = makeMock(opts)
    _ug_loadedModules = {}
    dofile(INTERFACE)
    local S = __SZTF3F6
    expect(S and S.protocol == 6, "interface did not expose protocol 6 state")
    S.player = 1
    S.world = 2
    S.nameScope = "mock"
    return S, api
end

local function completePart(S) return S.industryIndex ~= nil and S.industryIndex.complete == true end

local function dispatch(S, command)
    local ok, result = pcall(S.dispatch, command)
    expect(ok, "dispatch failed: " .. tostring(result))
    return result
end

local function fetchPart(S, part)
    local offset, pages, seen, complete, total = 0, 0, {}, false, nil
    while true do
        local result = dispatch(S, { op = "industry_catalog", request = part .. pages, offset = offset, limit = 100, part = part })
        expect(#result <= BUDGET, part .. " page exceeds byte budget: " .. #result)
        local pageTotal = tonumber(result:match('"total":(%d+)'))
        expect(pageTotal ~= nil, part .. " page has no total")
        total = total or pageTotal
        local count = 0
        if part == "industry" then
            for id in result:gmatch('"entityId":"(%d+)"') do
                count = count + 1
                expect(not seen[id], "duplicate industry " .. id)
                seen[id] = true
            end
        elseif part == "town" then
            for id in result:gmatch('"entityId":"(%d+)"') do
                count = count + 1
                expect(not seen[id], "duplicate town " .. id)
                seen[id] = true
            end
        else
            local slice = result:match('"cargo":%[(.-)%]') or ""
            for id in slice:gmatch('"id":"(%d+)"') do
                count = count + 1
                expect(not seen[id], "duplicate cargo " .. id)
                seen[id] = true
            end
        end
        complete = result:find('"complete":true', 1, true) ~= nil
        pages = pages + 1
        if offset + count >= total then break end
        expect(count > 0, part .. " page did not advance")
        offset = offset + count
        expect(pages < 200, part .. " produced too many pages")
    end
    local unique = 0
    for _ in pairs(seen) do unique = unique + 1 end
    expect(unique == total, part .. " unique count " .. unique .. " != total " .. total)
    return total, unique, complete
end

-- 1. Plain function bindings, recipe roles, town statuses, incomplete data and scope guards.
do
    local S, mock = load({ count = 6, towns = 3, unreadTown = true })
    local result = dispatch(S, { op = "industry_catalog", request = "roles", offset = 0, limit = 100, part = "industry" })
    expect(result:find('"rolesVerified":true', 1, true) ~= nil, "plain function did not verify recipe roles")
    expect(result:find('"role":"boost"', 1, true) ~= nil, "booster input role was not parsed")
    local industries = fetchPart(S, "industry")
    local towns = fetchPart(S, "town")
    local cargo = fetchPart(S, "cargo")
    -- Cargo id 0 is the passenger id and is present in the resource
    -- catalogue, so it must survive the whole pipeline.
    expect(industries == 6 and towns == 3 and cargo == 9, "basic counts mismatch")
    local townResult = dispatch(S, { op = "industry_catalog", request = "town-status", offset = 0, limit = 100, part = "town" })
    expect(townResult:find('"status":"active"', 1, true) ~= nil, "active town demand missing")
    expect(townResult:find('"status":"zero"', 1, true) ~= nil, "zero town demand missing")
    expect(townResult:find('"needsStatus":"unread"', 1, true) ~= nil, "unread town demand missing")
    expect(not completePart(S), "unread town was reported as complete")
    mock.state.world = 3
    local ok, message = pcall(S.dispatch, { op = "industry_catalog", request = "scope", offset = 0, limit = 100, part = "industry" })
    expect(not ok, "world change was not rejected")
    expect(tostring(message):find("地图", 1, true) ~= nil, "world change error is not explicit")
    mock.state.world = 2
    local okWorld = pcall(S.dispatch, { op = "industry_catalog", request = "scope2", world = 99, offset = 0, limit = 100, part = "industry" })
    expect(not okWorld, "stale request world was not rejected")
    local before = S.industryIndex and S.industryIndex.revision or 0
    local refresh = dispatch(S, { op = "industry_refresh", request = "refresh" })
    local revision = tonumber(refresh:match('"revision":(%d+)'))
    expect(revision == before + 1, "refresh did not advance revision")
end

-- 2. One unread recipe keeps the catalogue incomplete.
do
    local S = load({ count = 4, towns = 2, failAt = 2 })
    local result = dispatch(S, { op = "industry_catalog", request = "partial", offset = 0, limit = 100, part = "industry" })
    expect(result:find('"recipeStatus":"unavailable"', 1, true) ~= nil, "failed recipe was not marked unavailable")
    expect(result:find('"complete":true', 1, true) == nil, "partial first page was reported complete")
    fetchPart(S, "industry"); fetchPart(S, "town"); local _, _, complete = fetchPart(S, "cargo")
    expect(not complete, "partial recipe was reported as complete")
    expect(S.industryIndex and S.industryIndex.complete == false, "cached index marked partial data complete")
end

-- 3. Protected callable tables require native proof; plain functions do not.
do
    local S = load({ count = 2, towns = 2, binding = "protected", proof = false })
    local result = dispatch(S, { op = "industry_catalog", request = "noproof", offset = 0, limit = 100, part = "industry" })
    expect(result:find('"recipeStatus":"unavailable"', 1, true) ~= nil, "unproved callable table was accepted")
    S = load({ count = 2, towns = 2, binding = "protected", proof = true })
    result = dispatch(S, { op = "industry_catalog", request = "proof", offset = 0, limit = 100, part = "industry" })
    expect(result:find('"recipeStatus":"verified"', 1, true) ~= nil, "proved callable table was not accepted")
    local townResult = dispatch(S, { op = "industry_catalog", request = "proof-town", offset = 0, limit = 100, part = "town" })
    expect(townResult:find('"districtStatus":"complete"', 1, true) ~= nil, "proved town center callable was not accepted")
end

-- 4. 100 industries with 150 inputs and 150 outputs must stay below the shared-memory cap.
do
    local S = load({ count = 100, big = true, towns = 2 })
    local industries = fetchPart(S, "industry")
    local towns = fetchPart(S, "town")
    local cargo = fetchPart(S, "cargo")
    expect(industries == 100 and towns == 2 and cargo == 301, "stress counts mismatch")
    expect(S.industryIndex and S.industryIndex.complete == true, "stress index did not complete")
end

-- 5. One readable demand district and one unparseable district must be partial.
do
    local S, mock = load({ count = 2, towns = 1 })
    local town = mock.engine.getComponent(2001, mock.type.ComponentType.TOWN)
    town.cargoNeeds = { { { 2, 100 } }, "unreadable district", {} }
    fetchPart(S, "industry"); fetchPart(S, "town"); fetchPart(S, "cargo")
    expect(S.industryIndex.complete == false, "partially unread town was reported complete")
    expect(S.industryIndex.unreadTownCount > 0, "partially unread town was omitted from pending count")
    expect(S.industryIndex.towns[1].needsStatus ~= "verified", "partially unread town needs were marked verified")
end

-- 6. A field-readable userdata vector must be accepted; an unreadable one must be partial.
do
    local S, mock = load({ count = 2, towns = 1 })
    local vector, isFile = makeNativeVector()
    local previous = debug.getmetatable(vector)
    debug.setmetatable(vector, { __index = { x = 100, y = 200, z = 0 } })
    mock.engine.util.town.getTownDistrictCenter = function() return vector end
    fetchPart(S, "industry"); fetchPart(S, "town"); fetchPart(S, "cargo")
    debug.setmetatable(vector, previous); if isFile then vector:close() end
    local town = S.industryIndex.towns[1]
    expect(town.districtStatus == "complete" and #town.districts > 0, "field-readable userdata district vector was rejected")

    S, mock = load({ count = 2, towns = 1 })
    vector, isFile = makeNativeVector(); previous = debug.getmetatable(vector)
    debug.setmetatable(vector, { __index = { z = 1 } })
    mock.engine.util.town.getTownDistrictCenter = function() return vector end
    fetchPart(S, "industry"); fetchPart(S, "town"); fetchPart(S, "cargo")
    debug.setmetatable(vector, previous); if isFile then vector:close() end
    town = S.industryIndex.towns[1]
    expect(town.districtStatus ~= "complete", "unreadable userdata vector was accepted")
end

-- 7. Empty outer demand table and readable-but-empty demand districts are valid.
do
    local S, mock = load({ count = 2, towns = 1 })
    local town = mock.engine.getComponent(2001, mock.type.ComponentType.TOWN)
    town.cargoNeeds = {}
    fetchPart(S, "industry"); fetchPart(S, "town"); fetchPart(S, "cargo")
    expect(S.industryIndex.towns[1].needsStatus == "verified", "empty outer demand table was marked unread")
    expect(S.industryIndex.complete == true, "empty outer demand table made the index incomplete")

    S, mock = load({ count = 2, towns = 1 })
    town = mock.engine.getComponent(2001, mock.type.ComponentType.TOWN)
    town.cargoNeeds = { {}, {}, {} }
    fetchPart(S, "industry"); fetchPart(S, "town"); fetchPart(S, "cargo")
    expect(S.industryIndex.towns[1].needsStatus == "verified", "valid empty demand districts were marked unread")
    expect(S.industryIndex.unreadTownCount == 0, "valid empty demand districts added a pending town")
    expect(S.industryIndex.complete == true, "valid empty demand districts made the index incomplete")
end

-- 8. The scrap regression: a cargo that only exists inside a rule must be
-- found as a downstream relation. Before the fix the official helper was the
-- only source of the direction and the rule map was read with ipairs, which
-- silently skipped every rule of a map that is keyed by rule index.
do
    local S, mock = load({
        count = 3, towns = 1,
        customRules = {
            [5001] = { [1] = { input = { { 1 }, { 9 } }, output = { [10] = 2 } } },
            [5002] = { [1] = { input = {}, output = { [9] = 1 } } },
        },
        customRecipes = {
            [5001] = { inputs = { 1, 9 }, outputs = { 10 } },
            [5002] = { inputs = {}, outputs = { 9 } },
        }
    })
    fetchPart(S, "industry"); fetchPart(S, "town"); fetchPart(S, "cargo")
    local industries = S.industryIndex.industries
    local mill, yard
    for _, entry in ipairs(industries) do
        if entry.entityId == "1001" then mill = entry
        elseif entry.entityId == "1002" then yard = entry end
    end
    expect(mill ~= nil and yard ~= nil, "scrap fixture industries are missing")
    -- The consumer of the scrap must be present in the rule derived inputs.
    local scrapInput
    for _, input in ipairs(mill.inputs) do if input.cargoId == "9" then scrapInput = input end end
    expect(scrapInput ~= nil, "the scrap input was lost in the rule parsing")
    expect(#mill.inputIds == 2, "the scrap input id was lost")
    expect(mill.inputStatus == "verified" and mill.outputStatus == "verified",
        "the scrap rule was not fully read")
    -- The rule has two alternative groups, so the cargo must be marked as an
    -- alternative and the alternatives must be counted.
    local hasAlternate = false
    for _, role in ipairs(scrapInput.roles or {}) do if role == "alternate" then hasAlternate = true end end
    expect(hasAlternate, "the alternative input group was not recorded")
    expect((scrapInput.alternatives or 0) >= 2, "the number of alternative groups was not recorded")
    -- Both the rule and the official helper are evidence for the same cargo,
    -- so both sources must be kept.
    local hasOfficial = false
    local hasRule = false
    for _, source in ipairs(scrapInput.sources or {}) do
        if source == "official" then hasOfficial = true end
        if source == "rule" then hasRule = true end
    end
    expect(hasOfficial, "the official source evidence was lost")
    expect(hasRule, "the rule source evidence was lost")
    expect(#(scrapInput.sources or {}) >= 2, "the source evidence of the same cargo was not merged")
    -- The scrap producer must be recognised as producing the scrap.
    local scrapOutput
    for _, output in ipairs(yard.outputs) do if output.cargoId == "9" then scrapOutput = output end end
    expect(scrapOutput ~= nil, "the scrap output was lost in the rule parsing")
    expect(#yard.outputIds == 1 and yard.outputIds[1] == "9", "the scrap output id was lost")
    -- A cargo that only appears in a rule must still be in the catalogue.
    local cargoIds = {}
    for _, cargo in ipairs(S.industryIndex.cargo) do cargoIds[cargo.id] = true end
    expect(cargoIds["9"] == true, "the scrap cargo is missing from the catalogue")
end

-- 9. Cargo identity: the catalogue decides which ids are real cargo. A
-- fractional id must never be rounded onto another cargo, an unknown id 0
-- must not be accepted and a catalogue listed id 0 must survive.
do
    -- Cargo 0 is listed in the catalogue and is referenced by a rule, so it
    -- must be accepted and must reach the catalogue listing.
    local S = load({
        count = 1, towns = 1,
        customRules = { [5001] = { [1] = { input = { { 0 } }, output = { [2] = 1 } } } },
        customRecipes = { [5001] = { inputs = { 0 }, outputs = { 2 } } }
    })
    fetchPart(S, "industry"); fetchPart(S, "town"); fetchPart(S, "cargo")
    local cargoIds = {}
    for _, cargo in ipairs(S.industryIndex.cargo) do cargoIds[cargo.id] = true end
    expect(cargoIds["0"] == true, "the catalogue listed cargo id 0 was dropped")
    expect(cargoIds["1"] == true, "cargo id 1 was dropped")
    local entry = S.industryIndex.industries[1]
    local found = false
    for _, id in ipairs(entry.inputIds) do if id == "0" then found = true end end
    expect(found, "the catalogue listed cargo id 0 was dropped from the rule")

    -- Cargo 0 is not listed in the catalogue, so a rule that mentions it
    -- must not turn it into a real cargo.
    S = load({
        count = 1, towns = 1, noPassengerCargo = true,
        customRules = { [5001] = { [1] = { input = { { 0 } }, output = { [2] = 1 } } } },
        customRecipes = { [5001] = { inputs = { 0 }, outputs = { 2 } } }
    })
    fetchPart(S, "industry"); fetchPart(S, "town"); fetchPart(S, "cargo")
    cargoIds = {}
    for _, cargo in ipairs(S.industryIndex.cargo) do cargoIds[cargo.id] = true end
    expect(cargoIds["0"] == nil, "an unlisted cargo id 0 was accepted")
    local unlisted = S.industryIndex.industries[1]
    for _, id in ipairs(unlisted.inputIds) do
        expect(id ~= "0", "an unlisted cargo id 0 became a rule input")
    end
end

-- 9b. The cargo catalogue is only sealed once every industry was read, so
-- a cargo that only appears in the rule of a later industry is still listed.
-- The map is larger than one page, so the last industry is only read on the
-- second page, and the ids are outside the resource catalogue so that only
-- the rule can contribute them.
do
    local S = load({
        count = 120, towns = 1,
        customRules = {
            [5120] = { [1] = { input = { { 21 } }, output = { [22] = 1 } } },
        },
        customRecipes = { [5120] = { inputs = { 21 }, outputs = { 22 } } }
    })
    fetchPart(S, "industry"); fetchPart(S, "town"); fetchPart(S, "cargo")
    local cargoIds = {}
    for _, cargo in ipairs(S.industryIndex.cargo) do cargoIds[cargo.id] = true end
    expect(cargoIds["21"] == true, "a cargo of a later industry is missing from the catalogue")
    expect(cargoIds["22"] == true, "an output cargo of a later industry is missing from the catalogue")
end

-- 10. A fractional cargo id and a non numeric id must not become a cargo.
do
    local S = load({
        count = 1, towns = 1,
        customRules = { [5001] = { [1] = { input = { { 1.5 } }, output = { ["abc"] = 1 } } } },
        customRecipes = { [5001] = { inputs = { 1.5 }, outputs = { "abc" } } }
    })
    fetchPart(S, "industry"); fetchPart(S, "town"); fetchPart(S, "cargo")
    local entry = S.industryIndex.industries[1]
    expect(#entry.inputIds == 0, "a fractional cargo id was rounded onto another cargo")
    expect(#entry.outputIds == 0, "a non numeric cargo id was accepted")
    expect(entry.unparsedRuleCount > 0, "an unexplainable rule entry was not counted")
end

-- 11. The read status of each direction is separate from the other one: a
-- readable but empty rule set is a confirmed empty result and must not be
-- reported as an unread rule.
do
    local S = load({
        count = 1, towns = 1,
        customRules = { [5001] = { [1] = { input = {}, output = {} } } },
        customRecipes = { [5001] = { inputs = {}, outputs = {} } }
    })
    fetchPart(S, "industry"); fetchPart(S, "town"); fetchPart(S, "cargo")
    local entry = S.industryIndex.industries[1]
    expect(entry.recipeStatus ~= "unavailable", "a readable empty rule set was reported as unread")
    expect(entry.inputStatus == "empty" or entry.inputStatus == "verified",
        "a readable empty input set was not reported as empty")
    expect(S.industryIndex.unreadIndustryCount == 0, "a confirmed empty rule set was counted as unread")
end

-- 12. The town demand is read from the documented district lists of
-- {cargo type id, amount} pairs, and the districts are residential,
-- commercial and industrial in that order.
do
    local S, mock = load({ count = 1, towns = 1 })
    local town = mock.engine.getComponent(2001, mock.type.ComponentType.TOWN)
    town.cargoNeeds = { { { { 2, 100 } } }, { { { 3, 0 } } }, { { { 4, 7 } } } }
    fetchPart(S, "industry"); fetchPart(S, "town"); fetchPart(S, "cargo")
    local entry = S.industryIndex.towns[1]
    expect(entry.needsStatus == "verified", "the documented district list was not read")
    expect(#entry.needs == 3, "the documented district list did not yield three cargo demands")
    local byId = {}
    for _, need in ipairs(entry.needs) do byId[need.cargoId] = need end
    expect(byId["2"] ~= nil and byId["2"].landUses[1] == 1, "the residential demand was not read")
    expect(byId["3"] ~= nil and byId["3"].status == "zero", "the zero commercial demand was not read")
    expect(byId["4"] ~= nil and byId["4"].landUses[1] == 3, "the industrial demand was not read")
    -- The district centre lookup must use the LandUseType member, because
    -- the numeric value of a district is not published.
    expect(mock.lastLandUse() ~= nil and type(mock.lastLandUse()) ~= "number",
        "the district centre was looked up with a positional number instead of the enum member")
end

-- 12b. A district that is a map from cargo type id to amount is still read,
-- and a cargo id is never derived from an array position.
do
    local S, mock = load({ count = 1, towns = 1 })
    local town = mock.engine.getComponent(2001, mock.type.ComponentType.TOWN)
    town.cargoNeeds = { { [7] = 100 }, { [8] = 0 }, {} }
    fetchPart(S, "industry"); fetchPart(S, "town"); fetchPart(S, "cargo")
    local entry = S.industryIndex.towns[1]
    expect(entry.needsStatus == "verified", "the district map was not read")
    expect(#entry.needs == 2, "the district map did not yield two cargo demands")
    local byId = {}
    for _, need in ipairs(entry.needs) do byId[need.cargoId] = need end
    expect(byId["7"] ~= nil and byId["8"] ~= nil, "the district map cargo ids were not read")
    -- The array positions 1 and 2 must not become cargo ids.
    expect(byId["1"] == nil and byId["2"] == nil, "an array position was mistaken for a cargo id")
end

-- 12c. A district list of pairs must not produce cargo ids from the
-- positions of the pairs either.
do
    local S, mock = load({ count = 1, towns = 1 })
    local town = mock.engine.getComponent(2001, mock.type.ComponentType.TOWN)
    town.cargoNeeds = { { { { 11, 100 }, { 12, 50 } } }, {}, {} }
    fetchPart(S, "industry"); fetchPart(S, "town"); fetchPart(S, "cargo")
    local entry = S.industryIndex.towns[1]
    expect(entry.needsStatus == "verified", "the pair list was not read")
    local byId = {}
    for _, need in ipairs(entry.needs) do byId[need.cargoId] = need end
    expect(byId["11"] ~= nil and byId["12"] ~= nil, "the pair list cargo ids were not read")
    expect(byId["1"] == nil and byId["2"] == nil, "a pair position was mistaken for a cargo id")
end

-- 12d. A district that mixes the documented pairs with a plain cargo id
-- must still yield exactly the real cargo ids. A reader that treats the
-- position of a demand as its cargo id would add cargo 1 and 2 here.
do
    local S, mock = load({ count = 1, towns = 1 })
    local town = mock.engine.getComponent(2001, mock.type.ComponentType.TOWN)
    town.cargoNeeds = { { { { 21, 100 } } }, { { 22, 5 } }, {} }
    fetchPart(S, "industry"); fetchPart(S, "town"); fetchPart(S, "cargo")
    local entry = S.industryIndex.towns[1]
    expect(entry.needsStatus == "verified", "the mixed district was not read")
    local byId = {}
    for _, need in ipairs(entry.needs) do byId[need.cargoId] = need end
    expect(byId["21"] ~= nil, "the pair demand was not read")
    expect(byId["22"] ~= nil, "the plain cargo id demand was not read")
    expect(byId["1"] == nil, "a demand position was mistaken for a cargo id")
    expect(byId["2"] == nil, "a demand position was mistaken for a cargo id")
end

-- 13. Rule.output is a map from CargoTypeId to amount. The keys are the
-- cargo ids and the values are quantities, so the values must never be
-- mistaken for cargo ids.
do
    local S = load({
        count = 1, towns = 1,
        customRules = { [5001] = { [1] = { input = { { 1 } }, output = { [10] = 3 } } } },
        customRecipes = { [5001] = { inputs = { 1 }, outputs = { 10 } } }
    })
    fetchPart(S, "industry"); fetchPart(S, "town"); fetchPart(S, "cargo")
    local entry = S.industryIndex.industries[1]
    expect(#entry.outputIds == 1, "the rule output map did not yield exactly one cargo")
    expect(entry.outputIds[1] == "10", "the rule output map was read with the wrong key")
    -- The quantity 3 must not become a cargo id.
    for _, id in ipairs(entry.outputIds) do
        expect(id ~= "3", "the produced amount was mistaken for a cargo id")
    end
end

-- 13b. A produced amount that happens to be the id of another cargo must
-- still not replace the real cargo id of the rule.
do
    local S = load({
        count = 1, towns = 1,
        customRules = { [5001] = { [1] = { input = { { 1 } }, output = { [3] = 1 } } } },
        customRecipes = { [5001] = { inputs = { 1 }, outputs = { 3 } } }
    })
    fetchPart(S, "industry"); fetchPart(S, "town"); fetchPart(S, "cargo")
    local entry = S.industryIndex.industries[1]
    expect(#entry.outputIds == 1, "the rule output map did not yield exactly one cargo")
    expect(entry.outputIds[1] == "3", "the produced amount was mistaken for the cargo id")
end

-- 14. The role of a cargo comes from the rules. The aggregated official
-- helper does not know the role, so it must not label a booster or an
-- alternate input as a required one.
do
    local S = load({
        count = 1, towns = 1,
        customRules = { [5001] = { [1] = { input = { { 4 } }, output = { [2] = 1 }, booster = true } } },
        customRecipes = { [5001] = { inputs = { 4 }, outputs = { 2 } } }
    })
    fetchPart(S, "industry"); fetchPart(S, "town"); fetchPart(S, "cargo")
    local entry = S.industryIndex.industries[1]
    local booster
    for _, input in ipairs(entry.inputs) do if input.cargoId == "4" then booster = input end end
    expect(booster ~= nil, "the booster input was lost")
    expect(booster.role == "boost", "the booster input was labelled as a required input")
    expect(booster.roleKnown == true, "the rule derived role was not marked as known")
end

-- 15. A rule set that could not be read must never be reported as a
-- confirmed direction, because the rule relations would be missing
-- silently even when the official aggregate could be read.
do
    local S, mock = load({
        count = 1, towns = 1,
        customRules = { [5001] = { [1] = { input = { { 1 } }, output = { [2] = 1 } } } },
        customRecipes = { [5001] = { inputs = { 1 }, outputs = { 2 } } }
    })
    -- The stock list of the industry becomes unreadable, so the rules are
    -- gone while the official helper still works.
    local original = mock.engine.getComponent
    mock.engine.getComponent = function(entity, componentType)
        if entity == 5001 and componentType == mock.type.ComponentType.STOCK_LIST then return nil end
        return original(entity, componentType)
    end
    fetchPart(S, "industry"); fetchPart(S, "town"); fetchPart(S, "cargo")
    local entry = S.industryIndex.industries[1]
    expect(entry.inputStatus ~= "verified", "an unread rule set was reported as a verified direction")
    expect(entry.outputStatus ~= "verified", "an unread rule set was reported as a verified direction")
    expect(entry.roleStatus ~= "verified", "an unread rule set was reported as a verified role")
end

-- 16. The aggregated official helper may also return a map keyed by cargo
-- id. The map shape must not be dropped.
do
    local S = load({
        count = 1, towns = 1,
        customRecipes = { [5001] = { inputs = { [5] = true }, outputs = { [6] = true } } }
    })
    fetchPart(S, "industry"); fetchPart(S, "town"); fetchPart(S, "cargo")
    local entry = S.industryIndex.industries[1]
    local found = false
    for _, id in ipairs(entry.inputIds) do if id == "5" then found = true end end
    expect(found, "a map shaped official result was dropped")
end

-- 17. The real API exposes CargoType and Rule as field-readable userdata.
-- getName returns a RESOURCE identifier; only CargoType.name is a display name.
-- These cases require native Lua (as used by CI), rather than a JS emulator.
local function nativeRecord(values)
    local value = assert(io.tmpfile(), "native userdata fixture unavailable")
    local previous = debug.getmetatable(value)
    debug.setmetatable(value, { __index = function(_, key)
        if values[key] == nil then error("unknown native field " .. tostring(key)) end
        return values[key]
    end })
    return value, function() debug.setmetatable(value, previous); value:close() end
end

do
    local S, mock = load({ count = 1, towns = 0 })
    local names = { [1] = "原油", [2] = "化学品", [3] = "肥料", [4] = "燃料", [5] = "专用催化剂" }
    local resources = { [0] = "::/cargos/passengers/passengers.cargo" }
    local keys = { "crude_oil", "chemicals", "fertilizer", "fuel" }
    for i, key in ipairs(keys) do resources[i] = "::/cargos/" .. key .. "/" .. key .. ".cargo" end
    resources[5] = "::/mods/native/special.cargo"
    local records, close = {}, {}
    for i, label in pairs(names) do records[i], close[i] = nativeRecord({ name = label, timeToDeliverInSeconds = 60 }) end
    mock.res.cargoTypeRep.get = function(id) return records[id] end
    mock.res.cargoTypeRep.getAll = function() return resources end
    mock.res.cargoTypeRep.getName = function(id) return resources[id] end
    fetchPart(S, "industry"); fetchPart(S, "town"); fetchPart(S, "cargo")
    local actual = {}; for _, cargo in ipairs(S.industryIndex.cargo) do actual[cargo.id] = cargo.name end
    for id, label in pairs(names) do
        expect(actual[tostring(id)] == label, "native cargo display name became a resource path: " .. tostring(actual[tostring(id)]))
    end
    -- The older cargo-addition catalogue must use the same display resolver.
    local state = S.tick(true)
    for _, dispose in pairs(close) do dispose() end
    expect(state:find('"name":"原油"', 1, true) ~= nil, "cargo-addition catalogue lost its Chinese display name")
end

-- Missing/English vanilla labels have a Chinese resource-name fallback.
-- A mod's localized label is preserved; an unknown resource path is not UI text.
do
    local S, mock = load({ count = 1, towns = 0 })
    local resources = { [0] = "::/cargos/passengers/passengers.cargo", [1] = "::/cargos/crude_oil/crude_oil.cargo",
        [2] = "::/cargos/fertilizer/fertilizer.cargo", [3] = "::/mods/custom/catalyst.cargo", [4] = "::/mods/custom/mystery.cargo" }
    mock.res.cargoTypeRep.getAll = function() return resources end
    mock.res.cargoTypeRep.getName = function(id) return resources[id] end
    mock.res.cargoTypeRep.get = function(id)
        if id == 1 then return { name = "Crude Oil" } end
        if id == 3 then return { name = "精炼催化剂" } end
        error("display resource unavailable")
    end
    fetchPart(S, "industry"); fetchPart(S, "town"); fetchPart(S, "cargo")
    local labels = {}; for _, cargo in ipairs(S.industryIndex.cargo) do labels[cargo.id] = cargo.name end
    expect(labels["1"] == "原油" and labels["2"] == "肥料", "vanilla cargo names were not localized")
    expect(labels["3"] == "精炼催化剂", "a mod display name was replaced by a fallback")
    expect(labels["4"] == "货物 4", "an unknown resource identifier leaked into UI")
end

-- Fertilizer is consumed by native booster rules even when the aggregated
-- production helper omits that optional material. Identity is cargo-ID based.
do
    local S, mock = load({ count = 5, towns = 0,
        customRules = { [5001] = { { input = { { 1 } }, output = { [6] = 1 } } }, [5005] = {} },
        customRecipes = { [5001] = { inputs = { 1 }, outputs = { 6 } }, [5002] = { inputs = {}, outputs = { 7 } },
            [5003] = { inputs = {}, outputs = { 7 } }, [5004] = { inputs = {}, outputs = { 7 } }, [5005] = { inputs = {}, outputs = {} } } })
    local close = {}
    for id = 5002, 5004 do
        local base, closeBase = nativeRecord({ input = {}, output = { [7] = 1 }, booster = false })
        local booster, closeBooster = nativeRecord({ input = { { 6 } }, output = {}, booster = true })
        mock.engine.getComponent(id, mock.type.ComponentType.STOCK_LIST).rules = { [1] = base, [8] = booster }
        close[#close + 1] = closeBase; close[#close + 1] = closeBooster
    end
    -- Capacity alone does not establish a consumption relationship.
    mock.engine.getComponent(5005, mock.type.ComponentType.STOCK_LIST).stocks = { { cargoType = 6, capacity = 100 } }
    fetchPart(S, "industry"); fetchPart(S, "town"); fetchPart(S, "cargo")
    local consumers = {}
    for _, industry in ipairs(S.industryIndex.industries) do
        for _, input in ipairs(industry.inputs) do
            if input.cargoId == "6" then
                consumers[#consumers + 1] = industry.entityId
                expect(input.role == "boost" and input.roleKnown, "fertilizer was not identified as an optional booster")
                expect(industry.inputStatus == "verified" and industry.outputStatus == "verified", "readable native rules remained unread")
            end
        end
    end
    for _, dispose in ipairs(close) do dispose() end
    table.sort(consumers)
    expect(table.concat(consumers, ",") == "1002,1003,1004", "fertilizer downstream lost native farms or included a storage-only target: " .. table.concat(consumers, ","))
    expect(S.industryIndex.complete and S.industryIndex.unreadIndustryCount == 0, "native rules made the complete catalogue partial")
end

-- An opaque native rule stays pending instead of crashing or pretending empty.
do
    local S, mock = load({ count = 1, towns = 0, customRecipes = { [5001] = { inputs = {}, outputs = {} } } })
    local value, dispose = nativeRecord({ unknown = true })
    mock.engine.getComponent(5001, mock.type.ComponentType.STOCK_LIST).rules = { value }
    fetchPart(S, "industry"); fetchPart(S, "town"); fetchPart(S, "cargo")
    dispose()
    expect(S.industryIndex.unreadIndustryCount == 1 and not S.industryIndex.complete, "an opaque native rule was silently accepted")
end

print("INDUSTRY_LUA_CHECK_OK")

