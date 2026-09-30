const { execSync } = require('child_process');
const https = require('https');
const PROJECT='acm-application-38298';const DB='(default)';
var BASE='https://firestore.googleapis.com/v1/projects/'+PROJECT+'/databases/'+DB+'/documents';
function getToken(){return execSync('gcloud auth print-access-token').toString().trim();}
function getDoc(coll, id){
  return new Promise(function(res, rej){
    var req = https.request(BASE + '/' + coll + '/' + id, { method:'GET', headers:{ Authorization:'Bearer '+getToken() } }, function(x){ var ch=''; x.on('data',function(c){ch+=c;}); x.on('end',function(){ try{ res(JSON.parse(ch)); }catch(e){ rej(e); } }); });
    req.on('error', rej); req.end();
  });
}
(async function(){
  console.log('=== LessonInvitation 4 full ===');
  try{ var d = await getDoc('LessonInvitations','4'); console.log(JSON.stringify(d.fields, null, 2)); }catch(e){ console.log('ERR '+e.message); }
  console.log('=== ReportChildren for child 3 (existing reports) ===');
  try{
    var body={structuredQuery:{from:[{collectionId:'ReportChildren'}],where:{fieldFilter:{field:{fieldPath:'ChildId'},op:'EQUAL',value:{integerValue:3}}}}};
    var data=JSON.stringify(body);
    var opt={method:'POST',headers:{Authorization:'Bearer '+getToken(),'Content-Type':'application/json'}};
    var r=await new Promise(function(res,rej){var rq=https.request(BASE+':runQuery',opt,function(x){var ch='';x.on('data',function(c){ch+=c;});x.on('end',function(){try{res(JSON.parse(ch));}catch(e){rej(e);}});});rq.on('error',rej);rq.write(data);rq.end();});
    var docs=r.map(function(x){if(!x.document)return null;var f=x.document.fields||{};var o={id:x.document.name.split('/').pop()};Object.keys(f).forEach(function(k){o[k]=f[k][Object.keys(f[k])[0]];});return o;}).filter(Boolean);
    console.log('count '+docs.length); docs.forEach(function(d){console.log(JSON.stringify(d));});
  }catch(e){ console.log('ERR '+e.message); }
})();
