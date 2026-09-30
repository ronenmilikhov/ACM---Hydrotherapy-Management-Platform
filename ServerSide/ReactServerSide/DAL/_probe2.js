const { execSync } = require('child_process');
const https = require('https');
const PROJECT = 'acm-application-38298';
const DB = '(default)';
var BASE = 'https://firestore.googleapis.com/v1/projects/' + PROJECT + '/databases/' + DB + '/documents';
function getToken() { return execSync('gcloud auth print-access-token').toString().trim(); }
function runQuery(collectionId, fieldPath, op, value, valueType) {
  var body = { structuredQuery: { from: [ { collectionId: collectionId } ], where: { fieldFilter: { field: { fieldPath: fieldPath }, op: op, value: {} } } } };
  body.structuredQuery.where.fieldFilter.value[valueType] = value;
  var data = JSON.stringify(body);
  var options = { method: 'POST', headers: { Authorization: 'Bearer ' + getToken(), 'Content-Type': 'application/json' } };
  return new Promise(function(resolve, reject) {
    var req = https.request(BASE + ':runQuery', options, function(res) {
      var chunks = '';
      res.on('data', function(c) { chunks += c; });
      res.on('end', function() { try { resolve(JSON.parse(chunks)); } catch (e) { reject(e); } });
    });
    req.on('error', reject);
    req.write(data);
    req.end();
  });
}
function docFields(doc) {
  if (!doc || !doc.document) return null;
  var f = doc.document.fields || {}; var out = { id: doc.document.name.split('/').pop() };
  Object.keys(f).forEach(function(k) { var v = f[k]; out[k] = v[Object.keys(v)[0]]; });
  return out;
}
(async function() {
  console.log('=== LessonInvitationRecipients for InvitationId 4 ===');
  try { var r = await runQuery('LessonInvitationRecipients', 'InvitationId', 'EQUAL', 4, 'integerValue'); var docs = r.map(docFields).filter(Boolean); console.log('recipients count: ' + docs.length); docs.forEach(function(d) { console.log(JSON.stringify(d)); }); } catch (e) { console.log('ERR', e.message); }
  console.log('=== Children list (first 30) ===');
  try {
    var body = { structuredQuery: { from: [ { collectionId: 'Children' } ], limit: 30 } };
    var data = JSON.stringify(body);
    var options = { method: 'POST', headers: { Authorization: 'Bearer ' + getToken(), 'Content-Type': 'application/json' } };
    var r2 = await new Promise(function(resolve, reject) {
      var req = https.request(BASE + ':runQuery', options, function(res) { var ch=''; res.on('data',function(c){ch+=c;}); res.on('end',function(){try{resolve(JSON.parse(ch));}catch(e){reject(e);}}); });
      req.on('error', reject); req.write(data); req.end();
    });
    var docs2 = r2.map(docFields).filter(Boolean);
    docs2.forEach(function(d) { console.log('id=' + d.id + ' FullName=' + d.FullName + ' ActiveGroupId=' + (d.ActiveGroupId||'')); });
  } catch (e) { console.log('ERR', e.message); }
})();
