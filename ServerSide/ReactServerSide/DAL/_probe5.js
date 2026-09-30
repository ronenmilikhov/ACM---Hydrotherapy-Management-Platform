const { execSync } = require('child_process');
const https = require('https');
const PROJECT='acm-application-38298';const DB='(default)';
var BASE='https://firestore.googleapis.com/v1/projects/'+PROJECT+'/databases/'+DB+'/documents';
function getToken(){return execSync('gcloud auth print-access-token').toString().trim();}
function docFields(doc){if(!doc||!doc.fields)return null;var f=doc.fields||{};var out={id:doc.name.split('/').pop()};Object.keys(f).forEach(function(k){var v=f[k];out[k]=v[Object.keys(v)[0]];});return out;}
function q(coll,fp,op,val,vt){var body={structuredQuery:{from:[{collectionId:coll}],where:{fieldFilter:{field:{fieldPath:fp},op:op,value:{}}}}};body.structuredQuery.where.fieldFilter.value[vt]=val;var data=JSON.stringify(body);var opt={method:'POST',headers:{Authorization:'Bearer '+getToken(),'Content-Type':'application/json'}};return new Promise(function(res,rej){var r=https.request(BASE+':runQuery',opt,function(x){var ch='';x.on('data',function(c){ch+=c;});x.on('end',function(){try{res(JSON.parse(ch));}catch(e){rej(e);}});});r.on('error',rej);r.write(data);r.end();});}
(async function(){
  console.log('=== Instructors (first 20) ===');
  try{var body={structuredQuery:{from:[{collectionId:'Instructors'}],limit:20}};var data=JSON.stringify(body);var opt={method:'POST',headers:{Authorization:'Bearer '+getToken(),'Content-Type':'application/json'}};var r=await new Promise(function(res,rej){var rq=https.request(BASE+':runQuery',opt,function(x){var ch='';x.on('data',function(c){ch+=c;});x.on('end',function(){try{res(JSON.parse(ch));}catch(e){rej(e);}});});rq.on('error',rej);rq.write(data);rq.end();});var docs=r.map(function(x){if(!x.document)return null;return docFields(x.document);}).filter(Boolean);console.log('count '+docs.length);docs.forEach(function(d){console.log(JSON.stringify(d));});}catch(e){console.log('ERR '+e.message);}
  console.log('=== InstructorGroups ===');
  try{var r2=await q('InstructorGroups','InstructorId','EQUAL',2,'integerValue');var d2=r2.map(function(x){if(!x.document)return null;return docFields(x.document);}).filter(Boolean);console.log('instructor 2 groups: '+JSON.stringify(d2));}catch(e){console.log('ERR '+e.message);}
})();

