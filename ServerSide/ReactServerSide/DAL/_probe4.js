const { execSync } = require('child_process');
const https = require('https');
const PROJECT = 'acm-application-38298';
const DB = '(default)';
var BASE = 'https://firestore.googleapis.com/v1/projects/' + PROJECT + '/databases/' + DB + '/documents';
function getToken() { return execSync('gcloud auth print-access-token').toString().trim(); }
function getDoc(coll, id) {
  return new Promise(function(resolve, reject) {
    var req = https.request(BASE + '/' + coll + '/' + id, { method: 'GET', headers: { Authorization: 'Bearer ' + getToken() } }, function(res) { var ch=''; res.on('data',function(c){ch+=c;}); res.on('end',function(){try{resolve(JSON.parse(ch));}catch(e){reject(e);}}); });
    req.on('error', reject); req.end();
  });
}
function docFields(doc) {
  if (!doc || !doc.fields) return null;
  var f = doc.fields || {}; var out = { id: doc.name.split('/').pop() };
  Object.keys(f).forEach(function(k) { var v = f[k]; out[k] = v[Object.keys(v)[0]]; });
  return out;
}
(async function() {
  console.log('=== Child 3 ===');
  try { var d = await getDoc('Children', '3'); console.log(JSON.stringify(docFields(d))); } catch (e) { console.log('ERR', e.message); }
  console.log('=== Instructor 2 ===');
  try { var d2 = await getDoc('Instructors', '2'); if(d2 && d2.fields) console.log(JSON.stringify(docFields(d2))); else console.log('no instructor doc (may be different id field)'); } catch (e) { console.log('ERR', e.message); }
  console.log('=== Groups collection (first 20) ===');
  try {
    var body = { structuredQuery: { from: [ { collectionId: 'Groups' } ], limit: 20 } };
    var data = JSON.stringify(body);
    var options = { method: 'POST', headers: { Authorization: 'Bearer ' + getToken(), 'Content-Type': 'application/json' } };
    var r = await new Promise(function(resolve, reject) {
      var req = https.request(BASE + ':runQuery', options, function(res) { var ch=''; res.on('data',function(c){ch+=c;}); res.on('end',function(){try{resolve(JSON.parse(ch));}catch(e){reject(e);}}); });
      req.on('error', reject); req.write(data); req.end();
    });
    var docs = r.map(function(x){ if(!x.document) return null; return docFields(x.document); }).filter(Boolean);
    console.log('groups count: ' + docs.length);
    docs.forEach(function(d){ console.log(JSON.stringify(d)); });
  } catch (e) { console.log('ERR', e.message); }
})();
