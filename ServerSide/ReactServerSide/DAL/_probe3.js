const { execSync } = require('child_process');
const https = require('https');
const PROJECT = 'acm-application-38298';
const DB = '(default)';
var BASE = 'https://firestore.googleapis.com/v1/projects/' + PROJECT + '/databases/' + DB + '/documents';
function getToken() { return execSync('gcloud auth print-access-token').toString().trim(); }
function getDoc(coll, id) {
  return new Promise(function(resolve, reject) {
    var req = https.request(BASE + '/' + coll + '/' + id, { method: 'GET', headers: { Authorization: 'Bearer ' + getToken() } }, function(res) {
      var ch=''; res.on('data',function(c){ch+=c;}); res.on('end',function(){try{resolve(JSON.parse(ch));}catch(e){reject(e);}});
    });
    req.on('error', reject); req.end();
  });
}
(async function() {
  console.log('=== Full Child document id=3 ===');
  try { var d = await getDoc('Children', '3'); console.log(JSON.stringify(d.fields, null, 2)); } catch (e) { console.log('ERR', e.message); }
  console.log('=== TrainingSessions on 2026-07-10 (scan by MeetingDate) ===');
  try {
    var body = { structuredQuery: { from: [ { collectionId: 'TrainingSessions' } ], where: { fieldFilter: { field: { fieldPath: 'MeetingDate' }, op: 'EQUAL', value: { stringValue: '2026-07-10' } } } } };
    var data = JSON.stringify(body);
    var options = { method: 'POST', headers: { Authorization: 'Bearer ' + getToken(), 'Content-Type': 'application/json' } };
    var r = await new Promise(function(resolve, reject) {
      var req = https.request(BASE + ':runQuery', options, function(res) { var ch=''; res.on('data',function(c){ch+=c;}); res.on('end',function(){try{resolve(JSON.parse(ch));}catch(e){reject(e);}}); });
      req.on('error', reject); req.write(data); req.end();
    });
    var docs = r.map(function(x){ if(!x.document) return null; var f=x.document.fields||{}; var o={id:x.document.name.split('/').pop()}; Object.keys(f).forEach(function(k){o[k]=f[k][Object.keys(f[k])[0]];}); return o; }).filter(Boolean);
    console.log('TrainingSessions on 2026-07-10 count: ' + docs.length);
    docs.forEach(function(d){ console.log(JSON.stringify(d)); });
  } catch (e) { console.log('ERR', e.message); }
})();
