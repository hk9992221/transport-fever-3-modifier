local S=(type(__SZTF3F6)=='table' and __SZTF3F6.protocol==6 and __SZTF3F6) or {protocol=6,version='1.1.7',settings={},backups={},jobs={},lastMessage='已连接游戏内存',lastError='',catalog={},stocks={},worlds={},tasks={},nextScan=0,commandCount=0}
S.version='1.1.7';S.protocol=6
local CT=api and api.type and api.type.ComponentType or {}
local isCallable
local function arr(t) return setmetatable(t or {},{jsonArray=true}) end
local function esc(s) return '"'..tostring(s):gsub('[%z\1-\31\\"]',function(c) if c=='"' then return '\\"' elseif c=='\\' then return '\\\\' else return string.format('\\u%04x',string.byte(c)) end end)..'"' end
local function json(v,seen)
 local t=type(v);if t=='nil' then return 'null' elseif t=='boolean' then return v and 'true' or 'false' elseif t=='number' then return v==v and math.abs(v)<math.huge and string.format('%.17g',v) or 'null' elseif t=='string' then return esc(v) elseif t=='table' then
 seen=seen or {};if seen[v] then return 'null' end;seen[v]=true;local r={};local array=getmetatable(v) and getmetatable(v).jsonArray
 if array then for i=1,#v do r[#r+1]=json(v[i],seen) end else for k,x in pairs(v) do r[#r+1]=esc(k)..':'..json(x,seen) end end
 seen[v]=nil;return (array and '[' or '{')..table.concat(r,',')..(array and ']' or '}') else return esc(tostring(v)) end
end
S.json=json
local function component(e,t) if not e or t==nil then return nil end;local ok,c=pcall(function()if api.engine.entityExists(e) then return api.engine.getComponent(e,t) end end);return ok and c or nil end
local function entities(t) local ok,v=pcall(api.engine.getEntitiesWithComponent,t);return ok and v or {} end
local function module(n) return type(_ug_loadedModules)=='table' and _ug_loadedModules['::/'..n..'.tl'] or nil end
local function now() return api.util.getApplicationTime() end
local function ownName(e)
 local n=component(e,CT.NAME)
 if n and type(n.name)=='string' and n.name:match('%S') then return n.name end
end
local function gameName(e)
 local getter=api and api.engine and api.engine.util and api.engine.util.getEntityName
 if isCallable and isCallable(getter) then
  local ok,value=pcall(getter,e)
  if ok and type(value)=='string' and value:match('%S') then return value end
 end
 local n=ownName(e);if n then return n end
 local ind=component(e,CT.INDUSTRY)
 if ind then return ownName(ind.construction) end
end
local function name(e,default)
 local n=gameName(e);if n then return n end
 local ok,label=pcall(function()
  local ind=component(e,CT.INDUSTRY);local c=ind and component(ind.construction,CT.CONSTRUCTION)
  if not c then return nil end
  local id=api.res.constructionRep.find(c.fileName)
  if id>=0 then local r=api.res.constructionRep.get(id);return r and r.description and r.description.name end
 end)
 if ok and type(label)=='string' and label~='' then return label end
 return default or '未命名'
end
local function scriptState(path) local ok,e=pcall(api.engine.system.gameScriptSystem.getEntityForGameScript,path);if not ok then return nil end;local c=component(e,CT.GAME_SCRIPT);return c and c.state end
local function send(c,callback)
 S.commandCount=S.commandCount+1
 api.cmd.sendCommand(c,function(data,success,result) if not success then S.lastError='游戏未执行该命令，请查看诊断';S.lastMessage=S.lastError end;if callback then callback(success,result) end end)
end
local function event(id,name,param) send(api.cmd.makeScriptingSendEventCmd('TF3Modifier',id,name,param)) end
local function book(amount,automatic,callback)
 if amount==0 then return end
 if automatic then S.pendingFinance={at=now(),done=false} end
 send(api.cmd.makeJournalBookAssetCmd(S.player,module('game_mechanics/finance/loan_util').makeInterest(math.floor(amount+.5),-1)),function(success) if automatic and S.pendingFinance then S.pendingFinance.done=true;S.pendingFinance.at=now()+.45 end;if callback then callback(success) end end)
end
local function setBalance(value) local a=component(S.player,CT.ACCOUNT);assert(a,'未找到公司账户');S.financeHold=true;book(value-a.balance,false,function()S.financeHold=nil;S.financeReset=true;S.financeResumeAt=now()+.45 end);if value==a.balance then S.financeHold=nil;S.financeReset=true end end
local function revision(e) return tostring(api.engine.getRevision(e)) end
local function tvmods(v) return {topSpeedScale=v.topSpeedScale,noiseScale=v.noiseScale,pollutionScale=v.pollutionScale,comfortScale=v.comfortScale} end
local function newmods(b)
 local m=api.type.TransportVehicle.Modifiers.new();for k,v in pairs(b) do m[k]=v end;return m
end
local continuous={income=true,funds=true,no_costs=true,productivity=true,industry_alive=true,vehicle_speed=true,comfort=true,no_emissions=true,calendar_freeze=true,calendar_speed=true,simulation_speed=true}
local supported={'income','funds','no_costs','clear_loans','balance','rank','productivity','industry_alive','vehicle_speed','comfort','no_emissions','calendar_freeze','calendar_speed','simulation_speed','date','days','experience','daytime','clouds','industry'}
local function path(p)
 local v=_G;for part in p:gmatch('[^.]+') do local ok,x=pcall(function()return v and v[part] end);if not ok then return nil end;v=x end;return v
end
local function finite(v) return type(v)=='number' and v==v and math.abs(v)<math.huge end
local function need(ok,why) assert(ok,why) end
isCallable=function(v)
 local t=type(v);if t=='function' then return true end;if t~='table' then return false end
 local proof=__SZTF3Callable6
 return type(proof)=='table' and getmetatable(proof)==nil and proof[v]==true
end
local function funcs(list) for _,p in ipairs(list) do need(isCallable(path(p)),'接口可调用性尚未通过验证 '..p) end end
local function fields(c,list,label)
 need(c~=nil,'未找到组件 '..label);for _,p in ipairs(list) do local v=c;for part in p:gmatch('[^.]+') do local ok,x=pcall(function()return v[part] end);need(ok,'字段不可读 '..label..'.'..p);v=x end;need(finite(v),'字段类型未通过 '..label..'.'..p) end
end
local function cameraCenter()
 local camera=api and api.gui and api.gui.camera
 local getter=camera and camera.getCenter
 need(isCallable(getter),'镜头中心读取接口不可用或未通过验证')
 local position=getter();fields(position,{'x','y'},'镜头中心');return {x=position.x,y=position.y}
end
local function cameraValue()
 local camera=api and api.gui and api.gui.camera
 local getter=camera and camera.getCameraData
 if not isCallable(getter) then return nil,'镜头读取接口不可用或未通过验证' end
 local ok,value=pcall(getter)
 if not ok or value==nil then return nil,tostring(value):gsub('^.-:%d+: ','') end
 local xOK,x=pcall(function()return value.x end);local yOK,y=pcall(function()return value.y end)
 if not (xOK and yOK and finite(x) and finite(y)) then return nil,'镜头数据缺少有效的中心坐标' end
 local data={x=x,y=y}
 local function part(name)local partOK,n=pcall(function()return value[name] end);if partOK and finite(n) then return n end end
 data.z=part('z');data.w=part('w');local pitch=part('q');if pitch==nil then pitch=part('v') end;data.v=pitch
 return data
end
local function cameraData()
 local data,why=cameraValue()
 if data then return data end
 -- getCameraData can fail transiently (loading, pause menu, follow camera)
 -- while getCenter still returns the look-at position. Keep the center so
 -- inventory recommendation and retries remain usable.
 local ok,center=pcall(cameraCenter)
 if ok then return center end
 error((why and why..'；' or '')..tostring(center):gsub('^.-:%d+: ',''),0)
end
local function cameraPose()
 local data=cameraData();fields(data,{'x','y','z','w','v'},'完整镜头');return data
end
local function completeCamera(v)
 return type(v)=='table' and finite(v.x) and finite(v.y) and finite(v.z) and finite(v.w) and finite(v.v)
end
local function readCamera()
 local value,why
 -- Retry the full API before degrading to center-only data so a transient
 -- frame cannot drop distance, rotation and pitch.
 for attempt=1,3 do
  local sample,reason=cameraValue()
  if sample then
   value=sample;if completeCamera(sample) then break end
  else
   why=reason
  end
 end
 if not value then
  local ok,center=pcall(cameraCenter)
  if ok then value=center
  else why=(why and why..'；' or '')..tostring(center):gsub('^.-:%d+: ','') end
 end
 local readOK=value~=nil
 local sameMap=S.cameraWorld==S.world
 if not sameMap then S.previousCamera=nil;S.cameraWorld=S.world end
 if readOK then
  if completeCamera(value) then
   S.camera=value;S.cameraError='';S.cameraAt=now();S.cameraFresh=true
  elseif sameMap and completeCamera(S.camera) then
   -- Keep the last complete pose while adopting a fresh center. The pose is
   -- not current, so mark it stale and let the UI require a retry before
   -- applying or saving it.
   S.camera={x=value.x,y=value.y,z=S.camera.z,w=S.camera.w,v=S.camera.v}
   S.cameraError='完整镜头参数暂未返回，显示上次成功的距离、旋转与俯仰'
   S.cameraAt=now();S.cameraFresh=false
  else
   S.camera=value;S.cameraError='';S.cameraAt=now();S.cameraFresh=false
  end
 else
  S.cameraError=tostring(why):gsub('^.-:%d+: ','');S.cameraFresh=false
  -- A single failed sample must not erase the last good pose. Clear it only
  -- when the map/player changed so data from another map cannot be reused.
  if not sameMap then S.camera={};S.cameraAt=nil end
 end
 return S.camera,readOK
end
local function cmd(p) funcs({'api.cmd.sendCommand','api.cmd.'..p}) end
local function cap(id,test)
 local ok,why=pcall(test);S.capabilities[id]={supported=ok,reason=ok and '' or tostring(why):gsub('^.-:%d+: ','')}
end
local function financeDeps()
 cmd('makeJournalBookAssetCmd');local m=module('game_mechanics/finance/loan_util');need(m and isCallable(m.makeInterest),'缺少贷款记账接口 makeInterest');fields(component(S.player,CT.ACCOUNT),{'balance','loan'},'ACCOUNT')
end
local function calendarDeps()
 cmd('makeGameSetCalendarSpeedCmd');cmd('makeGameSetSpeedCmd');fields(component(S.world,CT.GAME_SPEED),{'millisPerDay','speedup'},'GAME_SPEED')
end
local function eventDeps() cmd('makeScriptingSendEventCmd') end
local function refreshCapabilities()
 S.capabilities={};for _,id in ipairs(supported) do S.capabilities[id]={supported=false,reason='等待当前地图接口验证'} end
 local ok,why=pcall(function()funcs({'api.engine.entityExists','api.engine.getComponent','api.engine.getEntitiesWithComponent','api.engine.util.getPlayer','api.engine.util.getWorld','api.util.getApplicationTime'});fields(component(S.player,CT.ACCOUNT),{'balance','loan'},'ACCOUNT');fields(component(S.world,CT.GAME_TIME),{'gameTime','timeOfDaySec'},'GAME_TIME') end)
 if not ok then for _,id in ipairs(supported) do S.capabilities[id].reason=tostring(why):gsub('^.-:%d+: ','') end;return end
 cap('balance',financeDeps);cap('funds',financeDeps)
 for _,id in ipairs({'income','no_costs'}) do cap(id,function()financeDeps();funcs({'api.engine.util.finance.calcIncomeSince'}) end) end
 cap('clear_loans',function()financeDeps();eventDeps();funcs({'api.engine.system.gameScriptSystem.getEntityForGameScript'});local m=module('game_mechanics/finance/loan_util');need(isCallable(m.getFullLoanAndInterestPayBack),'缺少贷款结算接口 getFullLoanAndInterestPayBack');local st=scriptState('::/game_mechanics/finance/loan.gs');need(st and type(st.obtainedLoans)=='table','未读取到贷款列表') end)
 cap('experience',eventDeps)
 cap('rank',function()eventDeps();local m=module('game_mechanics/company/company_progression_util');need(m and isCallable(m.getCompanyProgressionState) and isCallable(m.getMaxRank) and isCallable(m.getXpNeededForRank),'缺少公司等级接口');local c=module('game_mechanics/company/company_util');need(c and isCallable(c.externalGetCompaniesState),'缺少公司规模接口');fields(m.getCompanyProgressionState(S.player),{'level','experience'},'公司等级');fields(c.externalGetCompaniesState(),{'basePopulation'},'公司规模') end)
 local vehicles=entities(CT.TRANSPORT_VEHICLE)
 local function vehicleDeps()
 cmd('makeVehicleSetModifiersCmd');funcs({'api.type.TransportVehicle.Modifiers.new'});need(CT.PLAYER_OWNED~=nil,'缺少载具归属组件 PLAYER_OWNED');local n=0
 for _,e in pairs(vehicles) do local own=component(e,CT.PLAYER_OWNED);if not own or own.player==S.player then fields(component(e,CT.TRANSPORT_VEHICLE),{'modifiers.topSpeedScale','modifiers.noiseScale','modifiers.pollutionScale','modifiers.comfortScale'},'TRANSPORT_VEHICLE');n=n+1 end end
 need(n>0,'当前公司没有可验证的载具；出现载具后自动重试')
 end
 for _,id in ipairs({'vehicle_speed','comfort','no_emissions'}) do cap(id,vehicleDeps) end
 local industries=entities(CT.INDUSTRY)
 cap('productivity',function()cmd('makeStockListSetModifiersCmd');funcs({'api.type.StockList.Modifiers.new'});local n=0;for _,e in pairs(industries) do local ind=component(e,CT.INDUSTRY);need(ind~=nil,'产业组件不可读');fields(component(ind.stockList,CT.STOCK_LIST),{'modifiers.productivity'},'STOCK_LIST');n=n+1 end;need(n>0,'当前地图没有可验证的产业；出现产业后自动重试') end)
 cap('industry_alive',function()cmd('makeIndustrySetManualDevelopmentCmd');local n=0;for _,e in pairs(industries) do local c=component(e,CT.INDUSTRY);need(c and type(c.manualDevelopment)=='boolean','产业 manualDevelopment 类型未通过');n=n+1 end;need(n>0,'当前地图没有可验证的产业；出现产业后自动重试') end)
 for _,id in ipairs({'calendar_freeze','calendar_speed','simulation_speed'}) do cap(id,calendarDeps) end
 local function dateDeps()cmd('makeGameSetDateCmd');funcs({'api.type.Date.new','api.engine.util.getCalendarDate'});fields(api.engine.util.getCalendarDate(component(S.world,CT.GAME_TIME).gameTime),{'year','month','day'},'日历日期') end
 cap('date',dateDeps);cap('days',dateDeps)
 cap('daytime',function()cmd('makeGameSetTimeOfDayCmd');fields(component(S.world,CT.GAME_TIME),{'timeOfDaySec'},'GAME_TIME') end)
 cap('clouds',function()cmd('makeGameSetCloudCoverageCmd');fields(component(S.world,CT.WORLD),{'cloudCoverage'},'WORLD') end)
 cap('add',function()cmd('debug.makeStockSetCargoAmountCmd');funcs({'api.res.cargoTypeRep.getAll','api.res.cargoTypeRep.get','api.res.cargoTypeRep.getName','api.engine.util.stock.getStockCargoTypes','api.engine.system.simEntityAtStockSystem.getStockCount','api.engine.system.simEntityAtStockSystem.getStockCountForCargoType'});need(CT.STOCK_LIST~=nil,'缺少库存组件 STOCK_LIST') end)
 cap('town_capacity',function()cmd('makeTownSetInitialLandUseCapacitiesCmd');local n=0;for _,e in pairs(entities(CT.TOWN)) do local c=component(e,CT.TOWN);need(c and c.initialLandUseCapacities and finite(c.initialLandUseCapacities[1]) and finite(c.initialLandUseCapacities[2]) and finite(c.initialLandUseCapacities[3]),'城镇土地容量类型未通过');n=n+1 end;need(n>0,'当前地图没有可验证的城镇') end)
 S.capabilities.industry_value=S.capabilities.productivity
 cap('camera_read',function()cameraData() end)
 cap('camera',function()funcs({'api.gui.camera.setCameraData','api.type.Vec5f.new'});fields(cameraData(),{'x','y','z','w','v'},'完整镜头') end)
 cap('focus',function()funcs({'api.gui.camera.focusEntity'});need(S.capabilities.camera.supported,S.capabilities.camera.reason) end)
 cap('industry',function()need(CT.INDUSTRY~=nil and CT.STOCK_LIST~=nil and CT.TOWN~=nil,'当前游戏缺少产业、库存或城镇组件');local n=0;for _,e in pairs(entities(CT.INDUSTRY)) do local c=component(e,CT.INDUSTRY);if c then n=n+1 end end;need(n>0,'当前地图没有可验证的产业；出现产业后自动重试') end)
 S.capabilities['return']=S.capabilities.camera
 cap('rename_entity',function()cmd('makeEntitySetNameCmd');need(CT.NAME~=nil,'当前游戏未提供名称组件 NAME') end)
 S.nextCapability=now()+3
end
local function requireCap(id)
 local c=S.capabilities[id];need(c and c.supported,c and c.reason or '尚未验证接口 '..tostring(id))
end
S.capabilities={};for _,id in ipairs(supported) do S.capabilities[id]={supported=false,reason='等待当前地图接口验证'} end
local function ensureBackup(kind,e,data)
 local key=kind..':'..e;local b=S.backups[key];if not b then b={kind=kind,id=e,data=data};S.backups[key]=b end;return b
end
local function applyVehicles()
 local enabled=S.settings.vehicle_speed or S.settings.comfort or S.settings.no_emissions
 for _,e in pairs(entities(CT.TRANSPORT_VEHICLE)) do
 local c=component(e,CT.TRANSPORT_VEHICLE);local own=component(e,CT.PLAYER_OWNED)
 if c and (not own or own.player==S.player) then
 local key='vehicle:'..e;local b=S.backups[key]
 if enabled then b=b or ensureBackup('vehicle',e,tvmods(c.modifiers));local m={};for k,v in pairs(b.data) do m[k]=v end
 if S.settings.vehicle_speed then m.topSpeedScale=b.data.topSpeedScale*S.settings.vehicle_speed end
 if S.settings.comfort then m.comfortScale=b.data.comfortScale*S.settings.comfort end
 if S.settings.no_emissions then m.noiseScale=0;m.pollutionScale=0 end
 send(api.cmd.makeVehicleSetModifiersCmd(e,newmods(m)))
 elseif b then send(api.cmd.makeVehicleSetModifiersCmd(e,newmods(b.data)));S.backups[key]=nil end end end
end
local function applyIndustries()
 for _,e in pairs(entities(CT.INDUSTRY)) do
 local c=component(e,CT.INDUSTRY)
 if c then
 local s=component(c.stockList,CT.STOCK_LIST);local key='productivity:'..c.stockList;local b=S.backups[key]
 if s and S.settings.productivity then b=b or ensureBackup('productivity',c.stockList,s.modifiers.productivity);local m=api.type.StockList.Modifiers.new();m.productivity=b.data*S.settings.productivity;send(api.cmd.makeStockListSetModifiersCmd(c.stockList,m))
 elseif b then local m=api.type.StockList.Modifiers.new();m.productivity=b.data;send(api.cmd.makeStockListSetModifiersCmd(c.stockList,m));S.backups[key]=nil end
 key='industry:'..e;b=S.backups[key]
 if S.settings.industry_alive then b=b or ensureBackup('industry',e,c.manualDevelopment);send(api.cmd.makeIndustrySetManualDevelopmentCmd(e,true))
 elseif b then send(api.cmd.makeIndustrySetManualDevelopmentCmd(e,b.data));S.backups[key]=nil end
 end end
end
local function restoreAll()
 local b=S.speedBackup
 if b then send(api.cmd.makeGameSetCalendarSpeedCmd(b.day));send(api.cmd.makeGameSetSpeedCmd(b.speed));S.speedBackup=nil end
 S.settings={};applyVehicles();applyIndustries();S.fundsFloor=nil;S.financeTime=nil;S.nextScan=0
end
local function applyCalendar()
 local c=component(S.world,CT.GAME_SPEED);assert(c,'未找到时间组件')
 if not S.speedBackup then S.speedBackup={day=c.millisPerDay,speed=c.speedup} end
 local b=S.speedBackup
 send(api.cmd.makeGameSetCalendarSpeedCmd(S.settings.calendar_freeze and 0 or S.settings.calendar_speed and math.max(1,math.floor(b.day/S.settings.calendar_speed+.5)) or b.day))
 send(api.cmd.makeGameSetSpeedCmd(S.settings.simulation_speed or b.speed))
 if not S.settings.calendar_freeze and not S.settings.calendar_speed and not S.settings.simulation_speed then S.speedBackup=nil end
end
local function company() return module('game_mechanics/company/company_progression_util').getCompanyProgressionState(S.player) end
local function setRank(level)
 local m=module('game_mechanics/company/company_progression_util');level=math.floor(level);assert(level>=1 and level<=m.getMaxRank(),'公司等级必须在 1–15 之间')
 local s=company();assert(level>=s.level,'当前接口支持提升等级，降低等级需要读取原存档')
 local cs=module('game_mechanics/company/company_util').externalGetCompaniesState()
 local target=m.getXpNeededForRank(cs.basePopulation,level)
 if target>s.experience then event('Companies','_debugAddExperience',{entity=S.player,amount=target-s.experience}) end
 S.jobs[#S.jobs+1]={at=now()+.5,level=level};S.lastMessage='等级提升命令已提交'
end
local function stockPosition(owner)
 local ok,matrix=pcall(function()local con=component(owner.construction,CT.CONSTRUCTION);assert(con,'建筑没有可读取的建设组件');assert(con.transf,'建筑没有可读取的变换矩阵');return con.transf end)
 if not ok then return nil,tostring(matrix):gsub('^.-:%d+: ','') end
 local failures={}
 local function attempt(label,getter)
  local success,value=pcall(function()local p=getter();fields(p,{'x','y'},'建筑位置');return {x=p.x,y=p.y} end)
  if success then return value end;failures[#failures+1]=label..'：'..tostring(value):gsub('^.-:%d+: ','')
 end
 local function method(object,key,label,index)
  return attempt(label,function()
   local fn=object and object[key];assert(isCallable(fn),'接口不可调用或未验证 ('..type(fn)..')')
   return index and fn(matrix,index) or fn(matrix)
  end)
 end
 -- The public API documents both type helpers and instance methods. A binding
 -- can expose only one of them; protected callable tables still require proof.
 local mtOK,mt=pcall(function()return api.type.Mat4f end)
 local p
 if mtOK then
  p=method(mt,'getTransl','getTransl');if p then return p,'','Mat4f.getTransl' end
  p=method(mt,'cols','cols',4);if p then return p,'','Mat4f.cols' end
 end
 p=method(matrix,'getTransl','matrix.getTransl');if p then return p,'','matrix.getTransl' end
 p=method(matrix,'cols','matrix.cols',4);if p then return p,'','matrix.cols' end
 -- Construction.transf is the WORLD transform. Mat4f has 16 column-major
 -- elements, accessed with Lua's 1-based subscripts, not process offsets.
 -- Validate the complete affine matrix before taking its translation column.
 p=attempt('transf[13..15]',function()
  local values={};for i=1,16 do local v=matrix[i];assert(finite(v),'矩阵第 '..i..' 项不可读或不是有限数');values[i]=v end
  assert(math.abs(values[4])<.0001 and math.abs(values[8])<.0001 and math.abs(values[12])<.0001 and math.abs(values[16]-1)<.0001,'不是有效的世界仿射矩阵')
  return {x=values[13],y=values[14]}
 end)
 if p then return p,'','Construction.transf[13..15]' end
 return nil,'建设实体 '..tostring(owner.construction)..' / 矩阵 '..type(matrix)..'；'..table.concat(failures,'；')
end
-- Industry recommendation index. Matching is derived only from production
-- rules and town needs; stock compatibility, stock counts and building names
-- never decide supply or demand.
local function cargoKey(v)
 if finite(v) then v=math.floor(v) end
 if type(v)=='number' then return v>0 and v==math.floor(v) and tostring(v) or nil end
 if type(v)=='string' then local n=tonumber(v);return n and n>0 and n==math.floor(n) and tostring(math.floor(n)) or nil end
 return nil
end
local function refCargo(v)
 if type(v)=='table' then return cargoKey(v.cargoType or v.cargo or v.cargoId or v.type or v.id or v[1]) end
 return cargoKey(v)
end
local function cargoName(id)
 S.cargoNames=S.cargoNames or {}
 local key=cargoKey(id);if not key then return '未知货物' end
 local cached=S.cargoNames[key];if cached then return cached end
 local ok,value=pcall(function()local c=api.res.cargoTypeRep.get(tonumber(key));return c and c.name end)
 local label=ok and type(value)=='string' and value:match('%S') and value or ('货物 '..key)
 S.cargoNames[key]=label;return label
end
local function addCargoRef(out,seen,id,role,group)
 id=cargoKey(id);if not id then return end
 local current=seen[id]
 if current then
  if role=='required' or role=='output' then current.role=role end
  if group and not current.group then current.group=group end
  return
 end
 local entry={cargoId=id,role=role or 'required'}
 if group then entry.group=group end
 seen[id]=entry;out[#out+1]=entry
end
local function collectCargo(list,out,seen,role,group)
 if type(list)~='table' then return end
 for i,value in ipairs(list) do
  if type(value)=='table' then
   if value[1]~=nil and value.cargoType==nil and value.cargo==nil and value.cargoId==nil then
    for _,nested in ipairs(value) do addCargoRef(out,seen,refCargo(nested),'alternate',group or ('g'..i)) end
   else
    local nextRole=role
    if value.optional==true or value.alternate==true then nextRole='alternate' end
    if value.boost==true or value.booster==true or value.isBooster==true then nextRole='boost' end
    addCargoRef(out,seen,refCargo(value),nextRole,value.group or group or ('g'..i))
   end
  else addCargoRef(out,seen,value,role,group) end
 end
 if #out==0 then
  for key,value in pairs(list) do
   if type(key)=='number' then addCargoRef(out,seen,value,role,group)
   elseif type(value)=='table' then addCargoRef(out,seen,refCargo(value),role,group) end
  end
 end
end
local function industryRules(sl)
 local rules=sl and sl.rules
 if type(rules)~='table' then return nil,nil end
 local inputs,outputs,seenIn,seenOut={},{},{},{}
 local found=false
 local function addOutputs(value,role)
  if type(value)~='table' then return end
  -- Engine.Component.StockList.Rule.output is {CargoTypeId : amount}; the
  -- keys are cargo IDs and the values are quantities, not cargo IDs.
  for key in pairs(value) do local id=cargoKey(key);if id then addCargoRef(outputs,seenOut,id,role or 'output','rule-output');found=true end end
 end
 for _,rule in pairs(rules) do
  if type(rule)=='table' then
   local groups=rule.input
   if type(groups)=='table' then
    for index,group in ipairs(groups) do
     if type(group)=='table' then
      local ids={};for _,value in ipairs(group) do local id=cargoKey(value);if id then ids[#ids+1]=id end end
      local role=rule.booster==true and 'boost' or (#ids>1 and 'alternate' or 'required')
      for _,id in ipairs(ids) do addCargoRef(inputs,seenIn,id,role,'rule-'..index);found=true end
     else addCargoRef(inputs,seenIn,group,rule.booster==true and 'boost' or 'required','rule-'..index);found=true end
    end
   end
   addOutputs(rule.output,'output');addOutputs(rule.byproducts,'byproduct')
   local extra={{'optionalInputs','alternate'},{'alternateInputs','alternate'},{'boostInputs','boost'},{'boosters','boost'}}
   for _,field in ipairs(extra) do if rule[field[1]]~=nil then collectCargo(rule[field[1]],inputs,seenIn,field[2],field[1]);found=true end end
  end
 end
 if not found then return nil,nil end
 return inputs,outputs
end
local function mergeRules(inputs,outputs,detailInputs,detailOutputs)
 local byIn,byOut={},{}
 for _,entry in ipairs(inputs) do byIn[entry.cargoId]=entry end
 for _,entry in ipairs(detailInputs) do
  local current=byIn[entry.cargoId]
  if current then if entry.role~='required' then current.role=entry.role end else byIn[entry.cargoId]=entry;inputs[#inputs+1]=entry end
 end
 for _,entry in ipairs(outputs) do byOut[entry.cargoId]=entry end
 for _,entry in ipairs(detailOutputs) do
  if not byOut[entry.cargoId] then byOut[entry.cargoId]=entry;outputs[#outputs+1]=entry end
 end
end
local function readRecipe(ind)
 local sid=ind and ind.stockList
 local sl=component(sid,CT.STOCK_LIST)
 local inputs,outputs,seenIn,seenOut={},{},{},{}
 local fn=api and api.engine and api.engine.util and api.engine.util.stock and api.engine.util.stock.getInputsOutputsFromRules
 local official,reason=false,''
 if isCallable(fn) then
  local ok,a,b=pcall(fn,sid)
  if ok then
   local rawIn,rawOut=a,b
   if type(a)=='table' and (a.inputs~=nil or a.outputs~=nil) then rawIn=a.inputs;rawOut=a.outputs
   elseif type(b)~='table' then
    if type(a)=='table' and type(a[1])=='table' and type(a[2])=='table' then rawIn=a[1];rawOut=a[2] else rawIn=nil;rawOut=nil end
   end
   if type(rawIn)=='table' and type(rawOut)=='table' then
    collectCargo(rawIn,inputs,seenIn,'required','all');collectCargo(rawOut,outputs,seenOut,'output','all');official=true
   else reason='配方输入输出结构未识别' end
  else reason=tostring(a):gsub('^.-:%d+: ','') end
 else reason='配方读取接口不可用或未验证' end
 local detailInputs,detailOutputs=industryRules(sl)
 local rolesVerified=detailInputs~=nil or detailOutputs~=nil
 if rolesVerified then mergeRules(inputs,outputs,detailInputs or {},detailOutputs or {}) end
 local status=official and 'verified' or 'unavailable'
 return inputs,outputs,status,rolesVerified,reason
end
local function industryType(ind)
 local construction=ind and component(ind.construction,CT.CONSTRUCTION)
 if not construction then return 'unknown','类型未识别' end
 local key=tostring(construction.fileName or construction.name or '')
 if key=='' then key='unknown' end
 local label=nil
 local ok,id=pcall(function()return api.res.constructionRep.find(construction.fileName) end)
 if ok and finite(id) and id>=0 then
  local ok2,row=pcall(function()return api.res.constructionRep.get(id) end)
  if ok2 and row and row.description and type(row.description.name)=='string' and row.description.name:match('%S') then label=row.description.name end
 end
 return key,label or '类型未识别'
end
local function addTownNeed(needs,seen,id,landUse,status)
 id=cargoKey(id);if not id then return end
 local need=seen[id]
 if not need then need={cargoId=id,landUses=arr(),status='unknown'};seen[id]=need;needs[#needs+1]=need end
 if landUse then
  local found=false;for _,known in ipairs(need.landUses) do if known==landUse then found=true;break end end
  if not found then need.landUses[#need.landUses+1]=landUse end
 end
 if status=='active' then need.status='active' elseif status=='zero' and need.status~='active' then need.status='zero' end
end
local function readTownNeeds(town)
 local c=component(town,CT.TOWN)
 if not c or type(c.cargoNeeds)~='table' then return {},'unread' end
 local needs,seen={},{}
 local parsed,sawEntries=false,false
 for landUse,list in pairs(c.cargoNeeds) do
  sawEntries=true
  local use=tonumber(landUse)
  if use and use>=1 and use<=3 and type(list)=='table' then
   for _,entry in ipairs(list) do
    local id,amount,status
    if type(entry)=='table' then
     id=cargoKey(entry[1] or entry.cargoType or entry.cargo)
     amount=tonumber(entry[2] or entry.amount or entry.demand)
     if entry.enabled==false or entry.active==false then status='zero'
     elseif entry.enabled==true or entry.active==true then status='active' end
    else id=cargoKey(entry) end
    if id then
     if status==nil and amount~=nil then status=amount>0 and 'active' or 'zero' end
     addTownNeed(needs,seen,id,use,status);parsed=true
    end
   end
  end
 end
 if parsed or not sawEntries then return needs,'verified' end
 return needs,'unread'
end
local function districtCenter(town,landUse)
 local fn=api and api.engine and api.engine.util and api.engine.util.town and api.engine.util.town.getTownDistrictCenter
 if not isCallable(fn) then return nil,'城镇需求区坐标接口不可用或未验证' end
 local reasons={}
 for _,variant in ipairs({{town,landUse},{town,landUse,1},{town,{landUse}}}) do
  local ok,value=pcall(fn,variant[1],variant[2],variant[3])
  if ok and type(value)=='table' then
   local x=tonumber(value.x or value[1]);local y=tonumber(value.y or value[2])
   if finite(x) and finite(y) then return {x=x,y=y} end
  end
  reasons[#reasons+1]=tostring(value)
 end
 return nil,table.concat(reasons,'；')
end
local function readIndustryEntry(entity,seenCargo)
 local industry=component(entity,CT.INDUSTRY)
 local typeKey,typeName='unknown','类型未识别'
 local position,positionError,positionSource=nil,'',''
 local inputs,outputs,recipeStatus,rolesVerified,recipeError={},{},'unavailable',false,'产业组件不可读'
 local level,maxLevel=0,0
 if industry then
  typeKey,typeName=industryType(industry)
  position,positionError,positionSource=stockPosition(industry)
  inputs,outputs,recipeStatus,rolesVerified,recipeError=readRecipe(industry)
  level=industry.level or 0;maxLevel=industry.maxLevel or 0
 end
 local inputIds,outputIds=arr(),arr()
 for _,entry in ipairs(inputs) do inputIds[#inputIds+1]=entry.cargoId;seenCargo[entry.cargoId]=true end
 for _,entry in ipairs(outputs) do outputIds[#outputIds+1]=entry.cargoId;seenCargo[entry.cargoId]=true end
 return {id=tostring(entity),nameEntity=tostring(entity),category='产业',entityId=tostring(entity),
  constructionId=industry and industry.construction and tostring(industry.construction) or '',
  stockListId=industry and industry.stockList and tostring(industry.stockList) or '',typeKey=typeKey,typeName=typeName,
  gameName=gameName(entity) or '',name=name(entity,'产业'),position=position,positionStatus=position and 'ok' or 'unknown',
  positionError=positionError or '',positionSource=positionSource or '',level=level,maxLevel=maxLevel,
  recipeStatus=recipeStatus,rolesVerified=rolesVerified==true,recipeError=recipeError,
  inputs=arr(inputs),outputs=arr(outputs),inputIds=inputIds,outputIds=outputIds},recipeStatus
end
local function readTownEntry(entity)
 local town=component(entity,CT.TOWN)
 local needs,needsStatus={},'unread'
 if town then needs,needsStatus=readTownNeeds(entity) end
 local districts=arr();local districtStatus='complete';local seenUse={}
 for _,need in ipairs(needs) do for _,landUse in ipairs(need.landUses) do
  if not seenUse[landUse] then
   seenUse[landUse]=true;local center=districtCenter(entity,landUse)
   if center then districts[#districts+1]={landUse=landUse,x=center.x,y=center.y} else districtStatus='partial' end
  end
 end end
 return {id=tostring(entity),nameEntity=tostring(entity),category='城镇',entityId=tostring(entity),
  gameName=gameName(entity) or '',name=name(entity,'城镇'),needs=arr(needs),needsStatus=needsStatus,
  districts=districts,districtStatus=districtStatus,
  districtError=districtStatus=='partial' and '部分需求区中心未读取' or ''},needsStatus
end
local function newIndustryBuild()
 local industryEntities={}
 for _,entity in pairs(entities(CT.INDUSTRY)) do industryEntities[#industryEntities+1]=entity end
 table.sort(industryEntities)
 local townEntities={}
 for _,entity in pairs(entities(CT.TOWN)) do townEntities[#townEntities+1]=entity end
 table.sort(townEntities)
 local revision=((S.industryRevision or (S.industryIndex and S.industryIndex.revision)) or 0)+1
 S.industryRevision=revision
 return {revision=revision,generatedAt=now(),world=S.world,scope=S.nameScope,
  entities=industryEntities,townEntities=townEntities,position=0,industries=arr(),towns=arr(),cargo=arr(),
  seenCargo={},unreadIndustryCount=0,unreadTownCount=0,townsDone=false,complete=false}
end
local function buildIndustryCargo(build)
 local allOK,allCargo=pcall(function()return api.res.cargoTypeRep.getAll() end)
 if allOK and type(allCargo)=='table' then
  for id in pairs(allCargo) do local key=cargoKey(id);if key then build.seenCargo[key]=true end end
 end
 local cargoIds={};for key in pairs(build.seenCargo) do cargoIds[#cargoIds+1]=tonumber(key) end
 table.sort(cargoIds)
 for _,id in ipairs(cargoIds) do build.cargo[#build.cargo+1]={id=tostring(id),name=cargoName(id)} end
end
local function buildIndustryTowns(build)
 for _,entity in ipairs(build.townEntities) do
  local entry,status=readTownEntry(entity)
  build.towns[#build.towns+1]=entry
  if status=='unread' then build.unreadTownCount=build.unreadTownCount+1 end
 end
 build.townsDone=true
end
local function processIndustryBatch(build,target)
 while build.position<target do
  local entry,status=readIndustryEntry(build.entities[build.position+1],build.seenCargo)
  build.position=build.position+1;build.industries[#build.industries+1]=entry
  if status~='verified' then build.unreadIndustryCount=build.unreadIndustryCount+1 end
 end
end
local function finishIndustryBuild(build)
 if not build.townsDone then buildIndustryTowns(build) end
 if #build.cargo==0 then buildIndustryCargo(build) end
 build.industryCount=#build.industries;build.townCount=#build.towns
 build.complete=build.unreadIndustryCount==0 and build.unreadTownCount==0
 S.industryIndex={revision=build.revision,generatedAt=build.generatedAt,industries=build.industries,towns=build.towns,
  cargo=build.cargo,industryCount=build.industryCount,townCount=build.townCount,
  unreadIndustryCount=build.unreadIndustryCount,unreadTownCount=build.unreadTownCount,complete=build.complete}
 S.industryRevision=build.revision;S.industryIndexWorld=S.world;S.industryIndexScope=S.nameScope;S.industryBuild=nil
end
local function industryMeta()
 local source=S.industryIndex or S.industryBuild
 if not source then return nil end
 if S.industryIndex and (S.industryIndexWorld~=S.world or S.industryIndexScope~=S.nameScope) then return nil end
 if S.industryIndex then
  return {schemaVersion=1,world=S.world,nameScope=S.nameScope,revision=source.revision,complete=source.complete,
   industryCount=source.industryCount,townCount=source.townCount,generatedAt=source.generatedAt,
   unreadIndustryCount=source.unreadIndustryCount,unreadTownCount=source.unreadTownCount}
 end
 return {schemaVersion=1,world=S.world,nameScope=S.nameScope,revision=source.revision,complete=false,
  industryCount=#source.entities,townCount=#source.towns,generatedAt=source.generatedAt,
  unreadIndustryCount=source.unreadIndustryCount,unreadTownCount=source.unreadTownCount}
end
local function industryCatalogPage(c)
 local offset=math.max(0,math.floor(tonumber(c.offset) or 0));local limit=math.max(1,math.min(100,math.floor(tonumber(c.limit) or 100)))
 local index=S.industryIndex
 if index and S.industryIndexWorld==S.world and S.industryIndexScope==S.nameScope and not S.industryBuild then
  if offset>#index.industries then error('产业索引分页游标已失效，请刷新产业数据') end
  local items=arr();for i=offset+1,math.min(#index.industries,offset+limit) do items[#items+1]=index.industries[i] end
  return json({ok=true,request=c.request,world=S.world,nameScope=S.nameScope,revision=index.revision,
   catalog={schemaVersion=1,revision=index.revision,complete=index.complete,industryCount=index.industryCount,
    townCount=index.townCount,unreadIndustryCount=index.unreadIndustryCount,unreadTownCount=index.unreadTownCount,
    generatedAt=index.generatedAt,offset=offset,total=#index.industries,items=items,
    towns=offset==0 and index.towns or nil,cargo=offset==0 and index.cargo or nil,message='产业索引已读取'}})
 end
 local build=S.industryBuild
 if not build or build.world~=S.world or build.scope~=S.nameScope then
  if offset>0 then error('产业索引已切换，请重新读取产业数据') end
  build=newIndustryBuild();S.industryBuild=build
 end
 if offset>build.position then error('产业索引分页游标已失效，请刷新产业数据') end
 if offset==0 and not build.townsDone then buildIndustryTowns(build) end
 if offset==0 and #build.cargo==0 then buildIndustryCargo(build) end
 local target=math.min(#build.entities,offset+limit)
 if build.position<target then processIndustryBatch(build,target) end
 local complete=build.position>=#build.entities
 if complete and build.townsDone then finishIndustryBuild(build) end
 local items=arr();for i=offset+1,math.min(#build.industries,offset+limit) do items[#items+1]=build.industries[i] end
 local total=#build.entities
 local isComplete=complete and build.townsDone
 return json({ok=true,request=c.request,world=S.world,nameScope=S.nameScope,revision=build.revision,
  catalog={schemaVersion=1,revision=build.revision,complete=isComplete,industryCount=total,
   townCount=#build.towns,unreadIndustryCount=build.unreadIndustryCount,unreadTownCount=build.unreadTownCount,
   generatedAt=build.generatedAt,offset=offset,total=total,items=items,
   towns=offset==0 and build.towns or nil,cargo=offset==0 and build.cargo or nil,
   message=isComplete and '产业索引已读取' or '正在读取产业索引'}})
end
local function stockOptions()
 local r=arr();local seen={}
 -- Town itself has no stockList/construction fields. Warehouse and Industry do.
 for _,ct in ipairs({CT.WAREHOUSE,CT.INDUSTRY}) do for _,e in pairs(entities(ct)) do
 local c=component(e,ct);local sid=c and c.stockList or e
 local sl=component(sid,CT.STOCK_LIST)
 if sl and not seen[sid] then seen[sid]=true;local slots=arr()
 for k,st in pairs(sl.stocks) do local i=tonumber(k)-1;local types=api.engine.util.stock.getStockCargoTypes(sid,i);local allowed=arr();for _,v in pairs(types) do allowed[#allowed+1]=v end
 slots[#slots+1]={index=i,cargo=st.cargoType,mixed=st.mixedTypes==true,allowed=allowed,capacity=st.capacity,count=api.engine.system.simEntityAtStockSystem.getStockCount(sid,i),kind=tostring(st.type)} end
 local position,positionError,positionSource=stockPosition(c)
 r[#r+1]={id=tostring(sid),nameEntity=tostring(e),gameName=gameName(e) or '',name=name(e,ct==CT.WAREHOUSE and '仓库' or ct==CT.TOWN and '城镇库存' or '产业库存'),category=ct==CT.WAREHOUSE and '仓库' or ct==CT.TOWN and '城镇库存' or '产业',position=position,positionError=positionError,positionSource=positionSource,slots=slots}
 end end end;return r
end
local function scan()
 local cat=arr()
 for i,path in pairs(api.res.cargoTypeRep.getAll()) do if i~=0 then local c=api.res.cargoTypeRep.get(i);cat[#cat+1]={id=tostring(i),name=c.name,category='货物',description='交付时限 '..c.timeToDeliverInSeconds..' 秒',path=path} end end
 table.sort(cat,function(a,b)return a.name<b.name end);S.catalog=cat;S.stocks=stockOptions();S.worlds=arr()
 for _,e in pairs(entities(CT.TOWN)) do local c=component(e,CT.TOWN);S.worlds[#S.worlds+1]={id=tostring(e),gameName=gameName(e) or '',name=name(e,'城镇'),category='城镇',res=c.initialLandUseCapacities[1],commercial=c.initialLandUseCapacities[2],industry=c.initialLandUseCapacities[3],description='城镇土地容量与镜头定位'} end
 for _,e in pairs(entities(CT.INDUSTRY)) do local c=component(e,CT.INDUSTRY);local sl=component(c.stockList,CT.STOCK_LIST);S.worlds[#S.worlds+1]={id=tostring(e),gameName=gameName(e) or '',name=name(e,'产业'),category='产业',productivity=sl and sl.modifiers.productivity or 1,description='产业等级 '..c.level..' / '..c.maxLevel..' · 镜头定位'} end
 S.tasks=arr();local ms=scriptState('::/mission/mission.gs')
 if ms and ms.tasks then local util=module('mission/mission_framework/mission_framework_util')
 for _,t in pairs(ms.tasks) do local g=t.genericState
 if g and not g.completedTime then local n=g.config and g.config.getVTableScriptFnParams or g.uid;local desc='当前阶段：'..g.uid
 local ok,info=pcall(function()local vt=util.getVTable(g.config);local ctx=util.makeTaskContext(t,ms.guiTimeSeconds);return vt.getTaskInfo(ctx) end)
 if ok and type(info)=='table' then n=info.name or n;local p={};for _,v in ipairs(info.paragraphs or {}) do if v.text then p[#p+1]=v.text end end;desc=#p>0 and table.concat(p,'\n') or desc end
 S.tasks[#S.tasks+1]={id=g.uid,name=tostring(n),category='当前任务',description=tostring(desc),canComplete=false} end end end
 S.nextScan=now()+3
end
local function addCargo(c)
 local target=tonumber(c.target);local count=tonumber(c.count) or 1
 assert(finite(count) and count==math.floor(count) and count>=1 and count<=10000,'每种货物的数量应为 1–10000 的整数')
 assert(c.world==nil or tonumber(c.world)==S.world,'地图已经切换，请重新选择库存')
 assert(not S.cargoResult or S.cargoResult.status~='pending','上一批货物尚未确认，请等待游戏执行')
 local ids,unique={},{};local raw=c.ids or {c.id};assert(type(raw)=='table','请选择货物')
 for _,id in ipairs(raw) do local n=tonumber(id);assert(finite(n) and n>0 and n==math.floor(n),'旅客不能当作仓储货物添加');if not unique[n] then ids[#ids+1]=n;unique[n]=true end end
 assert(#ids>0 and #ids<=64,'请选择 1–64 种货物')
 local sl=component(target,CT.STOCK_LIST);assert(sl,'请选择当前地图的目标库存')
 local slots={};for k,st in pairs(sl.stocks) do
  local index=tonumber(k)-1;local used=api.engine.system.simEntityAtStockSystem.getStockCount(target,index)
  local allowed={};for _,id in pairs(api.engine.util.stock.getStockCargoTypes(target,index)) do if st.mixedTypes or used==0 or st.cargoType==id then allowed[id]=true end end
  slots[#slots+1]={index=index,allowed=allowed,free=math.max(0,math.floor(st.capacity-used)),mixed=st.mixedTypes==true,locked=nil}
 end
 table.sort(slots,function(a,b)return a.index<b.index end)
 local all,order,amounts,placements={},{},{},{}
 for cargo,id in ipairs(ids) do
  all[#all+1]=cargo;amounts[cargo]=0;placements[cargo]={};local compatible=false
  for _,slot in ipairs(slots) do if slot.allowed[id] then compatible=true;break end end
  assert(compatible,'目标库存不接收某种所选货物，请选择兼容库存')
 end
 for s=1,#slots do order[s]=s end
 local function match(active,units)
  local assigned,occupants={},{};for s=1,#slots do occupants[s]={} end;local place
  place=function(cargo,seen)
   for _,s in ipairs(order) do local slot=slots[s];if not seen[s] and units[s]>0 and slot.allowed[ids[cargo]] then
    seen[s]=true
    if #occupants[s]<units[s] then occupants[s][#occupants[s]+1]=cargo;assigned[cargo]=s;return true end
    for i,other in ipairs(occupants[s]) do if place(other,seen) then occupants[s][i]=cargo;assigned[cargo]=s;return true end end
   end end;return false
  end
  for _,cargo in ipairs(active) do place(cargo,{}) end;return assigned
 end
 local function take(cargo,s,amount)
  amounts[cargo]=amounts[cargo]+amount;placements[cargo][s]=(placements[cargo][s] or 0)+amount
  slots[s].free=slots[s].free-amount;if not slots[s].mixed then slots[s].locked=cargo end
 end
 local units={};for s,slot in ipairs(slots) do units[s]=slot.free<count and 0 or (slot.mixed and math.min(#ids,math.floor(slot.free/count)) or 1) end
 local full=match(all,units);for _,cargo in ipairs(all) do if full[cargo] then take(cargo,full[cargo],count) end end
 local waiting={};for _,cargo in ipairs(all) do if amounts[cargo]==0 then waiting[#waiting+1]=cargo end end
 local function degree(s)local n=0;for _ in pairs(slots[s].allowed)do n=n+1 end;return n end
 table.sort(order,function(a,b)local da,db=degree(a),degree(b);if da~=db then return da<db end;if slots[a].free~=slots[b].free then return slots[a].free>slots[b].free end;return a<b end)
 for s,slot in ipairs(slots) do if slot.free<1 or slot.locked then units[s]=0 else units[s]=slot.mixed and math.min(#waiting,slot.free) or 1 end end
 local partial=match(waiting,units)
 for _,s in ipairs(order) do local occupants={};for _,cargo in ipairs(waiting) do if partial[cargo]==s then occupants[#occupants+1]=cargo end end
  for i,cargo in ipairs(occupants) do take(cargo,s,math.min(count,math.floor(slots[s].free/(#occupants-i+1)))) end
 end
 for _,cargo in ipairs(all) do for _,s in ipairs(order) do local slot=slots[s]
  if amounts[cargo]<count and slot.free>0 and slot.allowed[ids[cargo]] and (slot.mixed or not slot.locked or slot.locked==cargo) then take(cargo,s,math.min(count-amounts[cargo],slot.free)) end
 end end
 local token=tostring(c.request or '');assert(#token<=80,'货物请求标识无效')
 local commands,planned={},0
 for cargo,id in ipairs(ids) do for s,slot in ipairs(slots) do local amount=placements[cargo][s] or 0;if amount>0 then
  local before=api.engine.system.simEntityAtStockSystem.getStockCountForCargoType(target,slot.index,id)
  commands[#commands+1]={command=api.cmd.debug.makeStockSetCargoAmountCmd(target,slot.index,before+amount,api.res.cargoTypeRep.getName(id)),id=tostring(id),amount=amount};planned=planned+amount
 end end end
 local result={request=token,world=S.world,status=#commands==0 and 'completed' or 'pending',remaining=#commands,applied=0,total=#commands,added=0,planned=planned,requested=#ids*count,addedIds=arr()};S.cargoResult=result
 S.lastMessage=#commands==0 and '库存已满，无需添加' or '货物已提交，等待游戏执行';S.nextScan=0;local addedSet={}
 local function done(success,entry)
  if S.cargoResult~=result or result.world~=S.world then return end
  result.remaining=result.remaining-1;if success then result.applied=result.applied+1;result.added=result.added+entry.amount;if not addedSet[entry.id] then addedSet[entry.id]=true;result.addedIds[#result.addedIds+1]=entry.id end end
  if result.remaining==0 then result.status=result.applied==result.total and 'completed' or 'failed';S.lastMessage='实际添加 '..result.added..' 件'..(planned<result.requested and '，超出的数量已按剩余库存空间调整' or '')..(result.status=='failed' and '；部分操作失败，请刷新库存' or '');if result.status=='failed' then S.lastError=S.lastMessage end end
  S.nextScan=0
 end
 for i=1,#commands do local entry=commands[i];local reported=false;local function finish(success)if reported then return end;reported=true;done(success,entry)end;local ok=pcall(send,entry.command,finish);if not ok then finish(false) end end
end
local function dateValue(n)
 n=math.floor(n);local y=math.floor(n/10000);local m=math.floor(n/100)%100;local d=n%100;assert(y>=1850 and y<=2500 and m>=1 and m<=12,'日期必须在 1850–2500 年之间');local days={31,((y%4==0 and y%100~=0)or y%400==0)and 29 or 28,31,30,31,30,31,31,30,31,30,31};assert(d>=1 and d<=days[m],'日期不存在');return api.type.Date.new(y,m,d)
end
local function confirmRename()
 local r=S.renameResult
 if not r or (r.status~='pending' and r.status~='unconfirmed') then return end
 if r.world~=S.world then S.renameResult=nil;return end
 local e=tonumber(r.id);local ind=component(e,CT.INDUSTRY)
 local matched=gameName(e)==r.name or ownName(e)==r.name or (ind and ownName(ind.construction)==r.name)
 local getter=api.engine.util.getEntityNameStem
 if not matched and isCallable(getter) then
  local ok,stem=pcall(getter,e)
  if ok then matched=stem==r.name or (type(stem)=='table' and stem[1]==r.name) end
 end
 if matched then
  r.status='confirmed';S.lastMessage='已保存名称：'..r.name;S.nextScan=0
 elseif r.status=='pending' and now()>=r.deadline then
  r.status='unconfirmed';S.lastMessage='游戏尚未回读到新名称，请恢复模拟后刷新目录'
 end
end
local function renameEntity(c)
 local e=tonumber(c.id)
 need(e and e==math.floor(e) and e>0 and (component(e,CT.TOWN) or component(e,CT.INDUSTRY)),'请选择当前地图中的城镇或产业')
 need(c.world==nil or tonumber(c.world)==S.world,'地图已经切换，请重新选择目标')
 need(type(c.name)=='string','名称必须是文本')
 local value=c.name:gsub('^%s+',''):gsub('%s+$','')
 need(#value>0 and #value<=256,'名称不能为空，且不能超过 256 个 UTF-8 字节')
 need(not value:find('[%z\1-\31\127]'),'名称不能包含换行或控制字符')
 local token=tostring(c.request or '')
 need(#token<=80,'重命名请求标识无效')
 local result={id=tostring(e),world=S.world,request=token,name=value,status='pending',deadline=now()+12}
 S.renameResult=result;S.lastMessage='正在保存名称，等待游戏确认…'
 -- Name this entity explicitly; do not rename a shared stem such as its town.
 send(api.cmd.makeEntitySetNameCmd(e,value,true),function(success)
  if S.renameResult~=result then return end
  if not success then
   result.status='failed';S.lastError='保存名称失败，游戏未接受命令';S.lastMessage=S.lastError
  else confirmRename() end
  S.nextScan=0
 end)
end
function S.dispatch(c)
 assert(type(c)=='table','请求格式错误');if c.op=='read' then return S.tick(true) end;assert(S.player and component(S.player,CT.ACCOUNT),'请进入可运行地图')
 refreshCapabilities();local requiredCapacity=c.op=='set' and c.id or c.op;if c.op=='industry_catalog' or c.op=='industry_refresh' then requiredCapacity='industry' end
 if c.op~='reset' and c.op~='refresh' and c.op~='camera_read' and not (c.op=='set' and not c.enabled) then requireCap(requiredCapacity) end
 S.lastError='';S.lastMessage='命令已提交'
 if c.op=='camera_read' then
 assert(c.world==nil or tonumber(c.world)==S.world,'地图或连接已切换，请重新读取镜头')
 assert(api.engine.util.getWorld()==S.world and api.engine.util.getPlayer()==S.player,'地图或连接已切换，请重新读取镜头')
 assert(type(c.request)=='string' and #c.request>0 and #c.request<=80,'镜头请求标识无效')
 local cam,fresh=readCamera();assert(fresh,S.cameraError)
 if c.refreshStocks then S.stocks=stockOptions() end
 S.lastMessage=c.refreshStocks and '已读取当前镜头并刷新库存位置' or '已读取当前镜头'
 return json({ok=true,request=c.request,world=S.world,nameScope=S.nameScope,camera=cam,cameraAt=S.cameraAt,cameraFresh=S.cameraFresh,stocks=c.refreshStocks and S.stocks or nil,message=S.lastMessage})
 elseif c.op=='read' then return S.tick(true) elseif c.op=='reset' then restoreAll();S.lastMessage='已关闭持续功能并提交恢复命令'
 elseif c.op=='set' then
 need(continuous[c.id],'未知持续功能');local value=tonumber(c.value) or 1;assert(value==value and value>=.05 and value<=100,'倍率应在 0.05–100 之间');if c.id=='simulation_speed' then need(value==math.floor(value),'模拟速度倍率必须为整数') end;S.settings[c.id]=c.enabled and value or nil
 if c.id=='funds' then local a=component(S.player,CT.ACCOUNT);S.fundsFloor=c.enabled and a.balance or nil
 elseif c.id=='income' or c.id=='no_costs' then S.financeTime=component(S.world,CT.GAME_TIME).gameTime;S.lastBalance=component(S.player,CT.ACCOUNT).balance;S.lastLoan=component(S.player,CT.ACCOUNT).loan
 elseif c.id:match('calendar') or c.id=='simulation_speed' then applyCalendar()
 elseif c.id=='productivity' or c.id=='industry_alive' then applyIndustries()
 else applyVehicles() end
 elseif c.op=='balance' then local v=math.floor(c.value);assert(v>=-1000000000 and v<=1000000000000,'结余超出范围');setBalance(v);if S.settings.funds then S.fundsFloor=v end
 elseif c.op=='rank' then setRank(c.value)
 elseif c.op=='experience' then assert(c.value>=0 and c.value<=100000000,'经验超出范围');event('Companies','_debugAddExperience',{entity=S.player,amount=math.floor(c.value)})
 elseif c.op=='clear_loans' then
 local state=scriptState('::/game_mechanics/finance/loan.gs');assert(state and state.obtainedLoans,'未找到贷款列表');assert(not S.financeHold,'上一笔资金操作尚未完成');local loans=state.obtainedLoans;local remaining=#loans;local refund=0;local m=module('game_mechanics/finance/loan_util')
 if remaining>0 then S.financeHold=true
 local function done(success,amount) if success then refund=refund+amount end;remaining=remaining-1;if remaining==0 then if refund>0 then book(refund,false,function()S.financeHold=nil;S.financeReset=true;S.financeResumeAt=now()+.45 end) else S.financeHold=nil;S.financeReset=true end end end
 for i,loan in ipairs(loans) do local payment,interest=m.getFullLoanAndInterestPayBack(loan);local amount=payment+(interest or 0);send(api.cmd.makeScriptingSendEventCmd('TF3Modifier','Loan','Repay',{i,loan}),function(success)done(success,amount)end) end end
 S.lastMessage=remaining>0 and '还款后补回资金的命令已提交' or '当前没有未清贷款'
 elseif c.op=='date' then send(api.cmd.makeGameSetDateCmd(dateValue(c.value)))
 elseif c.op=='days' then local gt=component(S.world,CT.GAME_TIME);local dt=api.engine.util.getCalendarDate(gt.gameTime);local leap=function(y)return (y%4==0 and y%100~=0)or y%400==0 end;local y,m,d=dt.year,dt.month,dt.day;local step=c.value>=0 and 1 or -1;local count=math.abs(math.floor(c.value));assert(count<=36500,'单次最多调整 36500 天');for i=1,count do local md={31,leap(y)and 29 or 28,31,30,31,30,31,31,30,31,30,31};d=d+step;if d>md[m] then d=1;m=m+1;if m>12 then m=1;y=y+1 end elseif d<1 then m=m-1;if m<1 then m=12;y=y-1 end;d=({31,leap(y)and 29 or 28,31,30,31,30,31,31,30,31,30,31})[m] end end;send(api.cmd.makeGameSetDateCmd(dateValue(y*10000+m*100+d)))
 elseif c.op=='daytime' then assert(c.value>=0 and c.value<24,'小时应在 0–24 之间');send(api.cmd.makeGameSetTimeOfDayCmd(math.floor(c.value*3600)))
 elseif c.op=='clouds' then assert(c.value>=0 and c.value<=1,'云量应在 0–1 之间');send(api.cmd.makeGameSetCloudCoverageCmd(c.value))
 elseif c.op=='add' then addCargo(c)
 elseif c.op=='rename_entity' then renameEntity(c)
 elseif c.op=='town_capacity' then local e=tonumber(c.id);assert(component(e,CT.TOWN),'城镇已不存在');local v={c.res,c.commercial,c.industry};for _,n in ipairs(v) do assert(n>=1 and n<=100000,'容量应在 1–100000 之间') end;send(api.cmd.makeTownSetInitialLandUseCapacitiesCmd(e,v));S.nextScan=0
 elseif c.op=='industry_value' then local e=tonumber(c.id);local ind=component(e,CT.INDUSTRY);assert(ind,'产业已不存在');assert(c.value>=.05 and c.value<=100,'倍率应在 0.05–100 之间');local m=api.type.StockList.Modifiers.new();m.productivity=c.value;send(api.cmd.makeStockListSetModifiersCmd(ind.stockList,m));S.nextScan=0
 elseif c.op=='focus' then local target=tonumber(c.id);assert(target and (component(target,CT.TOWN) or component(target,CT.INDUSTRY) or component(target,CT.WAREHOUSE)),'目标已不存在，请刷新后重试');S.previousCamera=cameraPose();api.gui.camera.focusEntity(target);S.lastMessage='已定位镜头'
 elseif c.op=='camera' then S.previousCamera=cameraPose();local v=api.type.Vec5f.new(c.x,c.y,c.z,c.w,c.v);api.gui.camera.setCameraData(v);S.lastMessage='已设置镜头'
 elseif c.op=='return' then assert(S.previousCamera,'还没有记录上一镜头位置');local v=S.previousCamera;S.previousCamera=cameraPose();api.gui.camera.setCameraData(api.type.Vec5f.new(v.x,v.y,v.z,v.w,v.v));S.lastMessage='已返回上一镜头位置'
 elseif c.op=='refresh' then S.nextScan=0;scan();S.lastMessage='已刷新地图目录'
 elseif c.op=='industry_catalog' then
  assert(type(c.request)=='string' and #c.request>0 and #c.request<=80,'产业目录请求标识无效')
  return industryCatalogPage(c)
 elseif c.op=='industry_refresh' then
  S.industryRevision=(S.industryIndex and S.industryIndex.revision) or S.industryRevision or 0
  S.industryIndex=nil;S.industryIndexWorld=nil;S.industryIndexScope=nil;S.industryBuild=nil
  local build=newIndustryBuild();S.industryBuild=build;S.lastMessage='正在刷新产业与城镇索引'
  return json({ok=true,request=c.request,world=S.world,nameScope=S.nameScope,revision=build.revision,message=S.lastMessage})
 else error('未知操作：'..tostring(c.op)) end
 return json({ok=true,message=S.lastMessage})
end
function S.tick(active)
 local coreOK,coreError=pcall(function()funcs({'api.engine.entityExists','api.engine.getComponent','api.engine.getEntitiesWithComponent','api.engine.util.getPlayer','api.engine.util.getWorld','api.util.getApplicationTime'}) end);if not coreOK then return json({playing=false,settings={},capabilities={},message='等待地图核心接口验证',error=tostring(coreError)}) end
 local ok,player=pcall(api.engine.util.getPlayer);local w=api.engine.util.getWorld();local a=ok and component(player,CT.ACCOUNT)
 if not a then S.settings={};S.backups={};S.jobs={};S.speedBackup=nil;S.pendingFinance=nil;S.renameResult=nil;S.cargoResult=nil;S.catalog=arr();S.stocks=arr();S.worlds=arr();S.nameScope=nil;S.player=nil;S.camera={};S.cameraError='';S.cameraAt=nil;S.cameraFresh=false;S.cameraWorld=nil;S.previousCamera=nil;S.industryIndex=nil;S.industryBuild=nil;S.industryIndexWorld=nil;S.industryIndexScope=nil;S.industryRevision=nil;return json({playing=false,settings={},capabilities=S.capabilities,message='请进入可运行地图'}) end
 if S.player and (S.player~=player or S.world~=w) then S.settings={};S.backups={};S.jobs={};S.speedBackup=nil;S.renameResult=nil;S.cargoResult=nil;S.catalog=arr();S.stocks=arr();S.worlds=arr();S.nameScope=nil;S.nextScan=0;S.nextCapability=0;S.camera={};S.cameraError='';S.cameraAt=nil;S.cameraFresh=false;S.cameraWorld=nil;S.previousCamera=nil;S.industryIndex=nil;S.industryBuild=nil;S.industryIndexWorld=nil;S.industryIndexScope=nil;S.industryRevision=nil end
 S.player=player;S.world=w
 if not S.nameScope then S.nameScope=string.format('%.9f:%s',now(),tostring(w)) end
 local baseOK,baseError=pcall(function()fields(a,{'balance','loan'},'ACCOUNT');fields(component(w,CT.GAME_TIME),{'gameTime','timeOfDaySec'},'GAME_TIME');fields(component(w,CT.GAME_SPEED),{'millisPerDay','speedup'},'GAME_SPEED') end);if not baseOK then return json({playing=false,settings={},capabilities={},message='当前地图组件尚未通过验证',error=tostring(baseError)}) end
 if not active and next(S.settings) then restoreAll() end
 local t=now();local gt=component(w,CT.GAME_TIME);local gs=component(w,CT.GAME_SPEED)
 if t>=(S.nextCapability or 0) then refreshCapabilities() end
 confirmRename()
 for i=#S.jobs,1,-1 do local job=S.jobs[i];if t>=job.at then event('Companies','applyLevel',{level=job.level});table.remove(S.jobs,i) end end
 if active then
 local delta=0
 if S.pendingFinance and S.pendingFinance.done and t>=S.pendingFinance.at then S.pendingFinance=nil end
 if not S.pendingFinance and not S.financeHold and t>=(S.financeResumeAt or 0) then
 if S.financeReset then S.financeReset=nil;S.financeTime=gt.gameTime;S.lastBalance=a.balance;S.lastLoan=a.loan;if S.settings.funds then S.fundsFloor=a.balance end end
 if S.settings.income and S.financeTime then local inc=api.engine.util.finance.calcIncomeSince(S.financeTime,player);delta=delta+math.floor(inc*(S.settings.income-1)+.5) end
 if S.settings.no_costs and S.lastBalance then local inc=api.engine.util.finance.calcIncomeSince(S.financeTime or gt.gameTime,player);local loanChange=a.loan-(S.lastLoan or a.loan);local costs=S.lastBalance+inc+loanChange-a.balance;if costs>0 then delta=delta+costs end end
 if S.settings.income or S.settings.no_costs then S.financeTime=gt.gameTime;S.lastBalance=a.balance+delta;S.lastLoan=a.loan end
 if S.settings.funds then S.fundsFloor=math.max(S.fundsFloor or a.balance,a.balance+delta);if a.balance+delta<S.fundsFloor then delta=S.fundsFloor-a.balance end end
 if delta~=0 then book(delta,true) end
 end
 if t>=(S.nextMaintain or 0) then S.nextMaintain=t+3;if S.settings.vehicle_speed or S.settings.comfort or S.settings.no_emissions then applyVehicles() end;if S.settings.productivity or S.settings.industry_alive then applyIndustries() end end
 end
 if t>=S.nextScan then local success,err=pcall(scan);if not success then S.lastError=tostring(err);S.nextScan=t+3 end end
 local date={year=0,month=0,day=0};local dateOK,dateRead=pcall(function()return api.engine.util.getCalendarDate(gt.gameTime) end);if dateOK and dateRead then date=dateRead end;local rank={level=0,potentialLevel=0,experience=0};local rankOK,rankRead=pcall(company);if rankOK and rankRead then rank=rankRead end
 local cam=readCamera();local world=component(w,CT.WORLD)
 return json({playing=true,player=player,world=w,nameScope=S.nameScope,balance=a.balance,loan=a.loan,rank=rank.level,potentialRank=rank.potentialLevel,experience=rank.experience,date=string.format('%04d-%02d-%02d',date.year,date.month,date.day),dateNumber=date.year*10000+date.month*100+date.day,dayLength=gs.millisPerDay,speed=gs.speedup,daytime=gt.timeOfDaySec/3600,clouds=world and world.cloudCoverage or nil,settings=S.settings,capabilities=S.capabilities,catalog=S.catalog,stocks=S.stocks,worlds=S.worlds,tasks=S.tasks,camera=cam,cameraError=S.cameraError,cameraAt=S.cameraAt,cameraFresh=S.cameraFresh,renameResult=S.renameResult,cargoResult=S.cargoResult,industryIndex=industryMeta(),message=S.lastMessage,error=S.lastError,diagnostics={version=S.version,protocol=S.protocol,commands=S.commandCount,backups=(function()local n=0;for k in pairs(S.backups)do n=n+1 end;return n end)()}})
end
rawset(_G,'__SZTF3F6',S)
return '{}'
