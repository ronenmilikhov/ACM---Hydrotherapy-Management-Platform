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
  console.log('=== LessonInvitations on 2026-07-10 ===');
  try { var r = await runQuery('LessonInvitations', 'MeetingDate', 'EQUAL', '2026-07-10', 'stringValue'); var docs = r.map(docFields).filter(Boolean); if (docs.length === 0) console.log('NO LessonInvitations for 2026-07-10'); else docs.forEach(function(d) { console.log(JSON.stringify(d)); }); } catch (e) { console.log('ERR', e.message); }
  console.log('=== Children named דניאל לוי ===');
  try { var r2 = await runQuery('Children', 'FullName', 'EQUAL', 'דניאל לוי', 'stringValue'); var docs2 = r2.map(docFields).filter(Boolean); if (docs2.length === 0) console.log('No child דניאל לוי (exact)'); else docs2.forEach(function(d) { console.log(JSON.stringify(d)); }); } catch (e) { console.log('ERR', e.message); }
})();
