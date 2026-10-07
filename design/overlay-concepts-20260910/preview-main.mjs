const root=import.meta.dir;
const server=Bun.serve({hostname:"127.0.0.1",port:0,async fetch(r){
 const path=decodeURIComponent(new URL(r.url).pathname);
 if(path.includes(".."))return new Response(null,{status:404});
 const file=Bun.file(root+path);return await file.exists()?new Response(file):new Response(null,{status:404});
}});
try{
 await using view=new Bun.WebView({width:1500,height:1200});
 await view.navigate(server.url.href+"main-window.html");
 for(const mode of ["plan","board","inventory"]){
  await view.evaluate(`document.querySelector('[data-view="${mode}"]').click()`);
  await view.evaluate("document.querySelector('.shell').scrollIntoView()");
  await Bun.write(root+"/main-"+mode+".png",await view.screenshot());
  const result=await view.evaluate(`JSON.stringify({overflow:document.documentElement.scrollWidth>innerWidth,images:[...document.images].every(i=>i.complete&&i.naturalWidth>0)})`);
  console.log(mode,result);
 }
 await view.evaluate("(()=>{document.querySelector('[data-view=plan]').click();document.querySelector('#state').click()})()");
 await Bun.write(root+"/main-pending.png",await view.screenshot());
 await view.evaluate("document.querySelector('#idle').click()");
 if(!await view.evaluate("document.querySelector('#metrics').hidden&&document.querySelector('#content').hidden"))throw Error("Idle leaks current progress");
 await Bun.write(root+"/main-idle.png",await view.screenshot());
 await view.cdp("Emulation.setDeviceMetricsOverride",{width:390,height:844,deviceScaleFactor:1,mobile:false});
 await view.evaluate("(()=>{document.querySelector('#idle').click();document.querySelector('#state').click();window.scrollTo(0,0)})()");
 await Bun.write(root+"/main-mobile.png",await view.screenshot());
 if(await view.evaluate("document.documentElement.scrollWidth>innerWidth"))throw Error("Mobile overflow");
 console.log("MAIN_PREVIEW_PASS");
}finally{server.stop(true)}
