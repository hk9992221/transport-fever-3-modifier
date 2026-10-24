#ifndef SZ_TF3_CALLABLES_H
#define SZ_TF3_CALLABLES_H
/* Read metadata only. Lua 5.2 __call must be an actual function; table chains fail closed. */
static int luaFunctionValue(uintptr_t value){
 if(!readable(value,16))return 0;
 uint32_t tag=*(uint32_t*)(value+8);uintptr_t target=*(uintptr_t*)value;
 if((tag&15)!=6||!target)return 0;
 if(tag&64)return readable(target,16)&&((*(unsigned char*)(target+8)&15)==6);
 if((tag&63)==22){MEMORY_BASIC_INFORMATION info;
  return VirtualQuery((void*)target,&info,sizeof info)&&info.State==MEM_COMMIT&&!(info.Protect&PAGE_GUARD)&&(info.Protect&0xf0);
 }
 return 0;
}
static int tableCallable(uintptr_t value){
 if(!readable(value,16)||(*(uint32_t*)(value+8)&15)!=5)return 0;
 uintptr_t table=*(uintptr_t*)value;
 if(!readable(table,64)||(*(unsigned char*)(table+8)&15)!=5)return 0;
 uintptr_t meta=*(uintptr_t*)(table+16);
 /* lookup validates the real metatable GC type, node extent, string keys and tags. */
 uintptr_t method=lookup(meta,"__call");
 return method&&luaFunctionValue(method);
}
typedef struct {const char*expression;const char*keys[6];} CallablePath;
static const CallablePath callablePaths[]={
 {"api.cmd.makeEntitySetNameCmd",{"api","cmd","makeEntitySetNameCmd",NULL,NULL,NULL}},
 {"api.cmd.debug.makeStockSetCargoAmountCmd",{"api","cmd","debug","makeStockSetCargoAmountCmd",NULL,NULL}},
 {"api.cmd.makeGameSetCalendarSpeedCmd",{"api","cmd","makeGameSetCalendarSpeedCmd",NULL,NULL,NULL}},
 {"api.cmd.makeGameSetCloudCoverageCmd",{"api","cmd","makeGameSetCloudCoverageCmd",NULL,NULL,NULL}},
 {"api.cmd.makeGameSetDateCmd",{"api","cmd","makeGameSetDateCmd",NULL,NULL,NULL}},
 {"api.cmd.makeGameSetSpeedCmd",{"api","cmd","makeGameSetSpeedCmd",NULL,NULL,NULL}},
 {"api.cmd.makeGameSetTimeOfDayCmd",{"api","cmd","makeGameSetTimeOfDayCmd",NULL,NULL,NULL}},
 {"api.cmd.makeIndustrySetManualDevelopmentCmd",{"api","cmd","makeIndustrySetManualDevelopmentCmd",NULL,NULL,NULL}},
 {"api.cmd.makeJournalBookAssetCmd",{"api","cmd","makeJournalBookAssetCmd",NULL,NULL,NULL}},
 {"api.cmd.makeScriptingSendEventCmd",{"api","cmd","makeScriptingSendEventCmd",NULL,NULL,NULL}},
 {"api.cmd.makeStockListSetModifiersCmd",{"api","cmd","makeStockListSetModifiersCmd",NULL,NULL,NULL}},
 {"api.cmd.makeTownSetInitialLandUseCapacitiesCmd",{"api","cmd","makeTownSetInitialLandUseCapacitiesCmd",NULL,NULL,NULL}},
 {"api.cmd.makeVehicleSetModifiersCmd",{"api","cmd","makeVehicleSetModifiersCmd",NULL,NULL,NULL}},
 {"api.cmd.sendCommand",{"api","cmd","sendCommand",NULL,NULL,NULL}},
 {"api.engine.entityExists",{"api","engine","entityExists",NULL,NULL,NULL}},
 {"api.engine.getComponent",{"api","engine","getComponent",NULL,NULL,NULL}},
 {"api.engine.getEntitiesWithComponent",{"api","engine","getEntitiesWithComponent",NULL,NULL,NULL}},
 {"api.engine.system.gameScriptSystem.getEntityForGameScript",{"api","engine","system","gameScriptSystem","getEntityForGameScript",NULL}},
 {"api.engine.system.simEntityAtStockSystem.getStockCount",{"api","engine","system","simEntityAtStockSystem","getStockCount",NULL}},
 {"api.engine.system.simEntityAtStockSystem.getStockCountForCargoType",{"api","engine","system","simEntityAtStockSystem","getStockCountForCargoType",NULL}},
 {"api.engine.util.finance.calcIncomeSince",{"api","engine","util","finance","calcIncomeSince",NULL}},
 {"api.engine.util.getCalendarDate",{"api","engine","util","getCalendarDate",NULL,NULL}},
 {"api.engine.util.getEntityName",{"api","engine","util","getEntityName",NULL,NULL}},
 {"api.engine.util.getEntityNameStem",{"api","engine","util","getEntityNameStem",NULL,NULL}},
 {"api.engine.util.getPlayer",{"api","engine","util","getPlayer",NULL,NULL}},
 {"api.engine.util.getWorld",{"api","engine","util","getWorld",NULL,NULL}},
 {"api.engine.util.stock.getStockCargoTypes",{"api","engine","util","stock","getStockCargoTypes",NULL}},
 {"api.gui.camera.focusEntity",{"api","gui","camera","focusEntity",NULL,NULL}},
 {"api.gui.camera.getCameraData",{"api","gui","camera","getCameraData",NULL,NULL}},
 {"api.gui.camera.setCameraData",{"api","gui","camera","setCameraData",NULL,NULL}},
 {"api.res.cargoTypeRep.get",{"api","res","cargoTypeRep","get",NULL,NULL}},
 {"api.res.cargoTypeRep.getAll",{"api","res","cargoTypeRep","getAll",NULL,NULL}},
 {"api.res.cargoTypeRep.getName",{"api","res","cargoTypeRep","getName",NULL,NULL}},
 {"api.type.Date.new",{"api","type","Date","new",NULL,NULL}},
 {"api.type.StockList.Modifiers.new",{"api","type","StockList","Modifiers","new",NULL}},
 {"api.type.TransportVehicle.Modifiers.new",{"api","type","TransportVehicle","Modifiers","new",NULL}},
 {"api.type.Vec5f.new",{"api","type","Vec5f","new",NULL,NULL}},
 {"api.util.getApplicationTime",{"api","util","getApplicationTime",NULL,NULL,NULL}},
 {"_ug_loadedModules[\"::/game_mechanics/finance/loan_util.tl\"].makeInterest",{"_ug_loadedModules","::/game_mechanics/finance/loan_util.tl","makeInterest",NULL,NULL,NULL}},
 {"_ug_loadedModules[\"::/game_mechanics/finance/loan_util.tl\"].getFullLoanAndInterestPayBack",{"_ug_loadedModules","::/game_mechanics/finance/loan_util.tl","getFullLoanAndInterestPayBack",NULL,NULL,NULL}},
 {"_ug_loadedModules[\"::/game_mechanics/company/company_progression_util.tl\"].getCompanyProgressionState",{"_ug_loadedModules","::/game_mechanics/company/company_progression_util.tl","getCompanyProgressionState",NULL,NULL,NULL}},
 {"_ug_loadedModules[\"::/game_mechanics/company/company_progression_util.tl\"].getMaxRank",{"_ug_loadedModules","::/game_mechanics/company/company_progression_util.tl","getMaxRank",NULL,NULL,NULL}},
 {"_ug_loadedModules[\"::/game_mechanics/company/company_progression_util.tl\"].getXpNeededForRank",{"_ug_loadedModules","::/game_mechanics/company/company_progression_util.tl","getXpNeededForRank",NULL,NULL,NULL}},
 {"_ug_loadedModules[\"::/game_mechanics/company/company_util.tl\"].externalGetCompaniesState",{"_ug_loadedModules","::/game_mechanics/company/company_util.tl","externalGetCompaniesState",NULL,NULL,NULL}},
};
typedef struct {uintptr_t table,node;const char*key;} CallableCache;
static uintptr_t cachedLookup(CallableCache*cache,size_t*count,uintptr_t table,const char*key){
 for(size_t i=0;i<*count;i++)if(cache[i].table==table&&strcmp(cache[i].key,key)==0)return cache[i].node;
 uintptr_t node=lookup(table,key);
 if(*count<256){cache[*count].table=table;cache[*count].key=key;cache[*count].node=node;(*count)++;}
 return node;
}
static uintptr_t rawPathValue(CallableCache*cache,size_t*count,uintptr_t globals,const CallablePath*path){
 uintptr_t table=globals,node=0;
 for(size_t i=0;i<6&&path->keys[i];i++){
  if(!path->keys[i][0])return 0;
  node=cachedLookup(cache,count,table,path->keys[i]);if(!node)return 0;
  if(i+1<6&&path->keys[i+1]){
   if((*(uint32_t*)(node+8)&15)!=5)return 0;
   table=*(uintptr_t*)node;
  }
 }
 return node;
}
static uintptr_t callableGlobals(void*state){
 uintptr_t L=(uintptr_t)state;
 if(!readable(L,48)||(*(unsigned char*)(L+8)&15)!=8)return 0;
 uintptr_t g=*(uintptr_t*)(L+24);
 if(!readable(g,80)||(*(uint32_t*)(g+72)&15)!=5)return 0;
 uintptr_t reg=*(uintptr_t*)(g+64);
 if(!readable(reg,64)||(*(unsigned char*)(reg+8)&15)!=5||*(uint32_t*)(reg+56)<2)return 0;
 uintptr_t array=*(uintptr_t*)(reg+24);
 if(!readable(array,32)||(*(uint32_t*)(array+24)&15)!=5)return 0;
 return *(uintptr_t*)(array+16);
}
static int buildCallablePrefix(void*state,char*out,size_t capacity){
 static const char opening[]="do local __szproof={};";
 static const char closing[]="rawset(_G,'__SZTF3Callable6',__szproof) end\n";
 if(capacity<sizeof opening+sizeof closing)return 0;
 memcpy(out,opening,sizeof opening-1);size_t used=sizeof opening-1;
 uintptr_t globals=callableGlobals(state);CallableCache cache[256];size_t count=0;
 for(size_t i=0;globals&&i<sizeof(callablePaths)/sizeof(callablePaths[0]);i++){
  uintptr_t value=rawPathValue(cache,&count,globals,&callablePaths[i]);
  if(!value||!tableCallable(value))continue;
  int written=snprintf(out+used,capacity-used,"__szproof[%s]=true;",callablePaths[i].expression);
  if(written<0||(size_t)written>=capacity-used)return 0;
  used+=(size_t)written;
 }
 if(capacity-used<sizeof closing)return 0;
 memcpy(out+used,closing,sizeof closing);return 1;
}
#endif
