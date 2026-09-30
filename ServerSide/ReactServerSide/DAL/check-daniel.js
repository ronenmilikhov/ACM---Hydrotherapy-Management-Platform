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

async function getDocument(token, collection, docId) {
  const url = `https://firestore.googleapis.com/v1/projects/${projectId}/databases/(default)/documents/${collection}/${docId}`;
  const response = await fetch(url, {
    headers: { Authorization: `Bearer ${token}` }
  });
  const data = await response.json();
  console.log(`Document ${collection}/${docId}:`, JSON.stringify(data.fields, null, 2));
}

async function run() {
  try {
    const token = await getAccessToken();
    await getDocument(token, "Children", "3");
  } catch (error) {
    console.error(error);
  }
}

run();
