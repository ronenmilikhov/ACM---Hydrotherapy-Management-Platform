const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const projectId = "acm-application-38298";
const keyPath = path.join(__dirname, '..', 'gcp-key.json');

async function getAccessToken() {
  const key = JSON.parse(fs.readFileSync(keyPath, 'utf8'));
  const jwtHeader = Buffer.from(JSON.stringify({ alg: 'RS256', typ: 'JWT' })).toString('base64url');
  
  const now = Math.floor(Date.now() / 1000);
  const jwtClaim = Buffer.from(JSON.stringify({
    iss: key.client_email,
    scope: 'https://www.googleapis.com/auth/cloud-platform',
    aud: 'https://oauth2.googleapis.com/token',
    exp: now + 3600,
    iat: now
  })).toString('base64url');
  
  const sign = crypto.createSign('RSA-SHA256');
  sign.update(`${jwtHeader}.${jwtClaim}`);
  const signature = sign.sign(key.private_key, 'base64url');
  
  const assertion = `${jwtHeader}.${jwtClaim}.${signature}`;
  
  const response = await fetch('https://oauth2.googleapis.com/token', {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      grant_type: 'urn:ietf:params:oauth:grant-type:jwt-bearer',
      assertion
    })
  });
  
  const data = await response.json();
  return data.access_token;
}

async function checkTypes(token, collection) {
  const url = `https://firestore.googleapis.com/v1/projects/${projectId}/databases/(default)/documents/${collection}`;
  const res = await fetch(url, {
    headers: { Authorization: `Bearer ${token}` }
  });
  const data = await res.json();
  console.log(`=== Collection: ${collection} ===`);
  if (data.documents) {
    data.documents.forEach(doc => {
      const name = doc.name.split('/').pop();
      const fieldsTypes = {};
      for (const [key, val] of Object.entries(doc.fields)) {
        fieldsTypes[key] = Object.keys(val)[0];
      }
      console.log(`Doc ID: ${name} ->`, fieldsTypes);
    });
  } else {
    console.log(`No documents found in ${collection}.`);
  }
}

async function run() {
  try {
    const token = await getAccessToken();
    await checkTypes(token, "Children");
    await checkTypes(token, "Parents");
    await checkTypes(token, "Instructors");
  } catch (err) {
    console.error(err);
  }
}

run();
