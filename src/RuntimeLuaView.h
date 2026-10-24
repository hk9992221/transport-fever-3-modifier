#ifndef SZ_TF3_LUA_VIEW_H
#define SZ_TF3_LUA_VIEW_H
/* Only inspect a naturally supplied Lua state on its owning game thread.
   Layout is additionally gated by RuntimeSignatures.h before installing the hook. */
static int readable(uintptr_t address,size_t count){
 if(address<65536||!count||address>UINTPTR_MAX-count)return 0;
 uintptr_t end=address+count;
 while(address<end){MEMORY_BASIC_INFORMATION info;
  if(!VirtualQuery((void*)address,&info,sizeof info)||info.State!=MEM_COMMIT)return 0;
  DWORD protect=info.Protect&0xff;
  if((info.Protect&PAGE_GUARD)||protect==PAGE_NOACCESS||protect==PAGE_EXECUTE||!protect)return 0;
  uintptr_t region=(uintptr_t)info.BaseAddress;
  if(info.RegionSize>UINTPTR_MAX-region)return 0;
  uintptr_t next=region+info.RegionSize;if(next<=address)return 0;
  address=next<end?next:end;
 }
 return 1;
}
typedef struct {uintptr_t begin,end;} ReadableKeyRegion;
/* Lifetime is one lookup only. No page information or Lua pointer survives into another run. */
static int readableKey(uintptr_t address,size_t count,ReadableKeyRegion*region){
 if(address<65536||!count||address>UINTPTR_MAX-count)return 0;
 if(address>=region->begin&&address<region->end&&count<=region->end-address)return 1;
 MEMORY_BASIC_INFORMATION info;
 if(!VirtualQuery((void*)address,&info,sizeof info)||info.State!=MEM_COMMIT)return 0;
 DWORD protect=info.Protect&0xff;
 if((info.Protect&PAGE_GUARD)||protect==PAGE_NOACCESS||protect==PAGE_EXECUTE||!protect)return 0;
 uintptr_t begin=(uintptr_t)info.BaseAddress;
 if(info.RegionSize>UINTPTR_MAX-begin)return 0;
 uintptr_t end=begin+info.RegionSize;
 if(address+count<=end){region->begin=begin;region->end=end;return 1;}
 return readable(address,count);
}
static uintptr_t lookup(uintptr_t table,const char*key){
 if(!readable(table,64)||((*(unsigned char*)(table+8))&15)!=5)return 0;
 unsigned bits=*(unsigned char*)(table+11);
 /* A global namespace larger than 65536 nodes is not a verified target. */
 if(bits>16)return 0;
 uintptr_t nodes=*(uintptr_t*)(table+32);size_t count=(size_t)1<<bits;
 if(!readable(nodes,count*40))return 0;
 size_t len=strlen(key);ReadableKeyRegion keyRegion={0,0};
 for(size_t i=0;i<count;i++){
  uintptr_t node=nodes+i*40;
  if((*(uint32_t*)(node+24)&15)==4&&*(uint32_t*)(node+8)){
   uintptr_t str=*(uintptr_t*)(node+16);
   if(!readableKey(str,24+len,&keyRegion)||((*(unsigned char*)(str+8))&15)!=4)continue;
   if(*(size_t*)(str+16)==len&&memcmp((void*)(str+24),key,len)==0)return node;
  }
 }
 return 0;
}
static int isgui(void*state){
 uintptr_t L=(uintptr_t)state;
 if(!readable(L,48)||((*(unsigned char*)(L+8))&15)!=8)return 0;
 uintptr_t g=*(uintptr_t*)(L+24);
 if(!readable(g,80)||(*(uint32_t*)(g+72)&15)!=5)return 0;
 uintptr_t reg=*(uintptr_t*)(g+64);
 if(!readable(reg,64)||((*(unsigned char*)(reg+8))&15)!=5||*(uint32_t*)(reg+56)<2)return 0;
 uintptr_t arr=*(uintptr_t*)(reg+24);
 if(!readable(arr,32)||(*(uint32_t*)(arr+24)&15)!=5)return 0;
 uintptr_t G=*(uintptr_t*)(arr+16),a=lookup(G,"api"),app=lookup(G,"app");
 if(!a||!app||(*(uint32_t*)(a+8)&15)!=5||(*(uint32_t*)(app+8)&15)!=5)return 0;
 uintptr_t api=*(uintptr_t*)a;
 uintptr_t engine=lookup(api,"engine");
 if(!engine||(*(uint32_t*)(engine+8)&15)!=5)return 0;
 uintptr_t gui=lookup(api,"gui");
 return gui&&(*(uint32_t*)(gui+8)&15)==5&&!lookup(api,"GameScriptTableHelper");
}
#endif
