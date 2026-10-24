#ifndef SZ_TF3_SCAN_H
#define SZ_TF3_SCAN_H
/* Portable scanner also compiled by the isolated fixture. Never accepts a second hit. */
static int sz_match(const unsigned char *candidate,const unsigned char *pattern,const unsigned char *mask,size_t length){
 for(size_t i=0;i<length;i++)if((candidate[i]&mask[i])!=(pattern[i]&mask[i]))return 0;
 return 1;
}
static unsigned sz_scan(const unsigned char *bytes,size_t size,const unsigned char *pattern,const unsigned char *mask,size_t length,const unsigned char **found){
 unsigned hits=0;
 if(!length||size<length||mask[0]!=255)return 0;
 const unsigned char *p=bytes,*end=bytes+size-length+1;
 while(p<end){p=memchr(p,pattern[0],(size_t)(end-p));if(!p)break;
  if(sz_match(p,pattern,mask,length)){*found=p;if(++hits>1)return hits;}p++;
 }
 return hits;
}
#endif
