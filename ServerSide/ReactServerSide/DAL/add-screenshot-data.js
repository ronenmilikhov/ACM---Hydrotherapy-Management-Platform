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

async function createDocument(token, collection, docId, fields) {
  const url = `https://firestore.googleapis.com/v1/projects/${projectId}/databases/(default)/documents/${collection}/${docId}`;
  
  const formattedFields = {};
  for (const [key, value] of Object.entries(fields)) {
    if (typeof value === 'boolean') {
      formattedFields[key] = { booleanValue: value };
    } else if (typeof value === 'number') {
      formattedFields[key] = { integerValue: value.toString() };
    } else {
      formattedFields[key] = { stringValue: value };
    }
  }

  const res = await fetch(url, {
    method: 'PATCH',
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({ fields: formattedFields })
  });

  const data = await res.json();
  if (!res.ok) {
    console.error(`Failed to create doc in ${collection}/${docId}:`, data);
  } else {
    console.log(`Successfully created/updated ${collection}/${docId}`);
  }
}

async function run() {
  try {
    const token = await getAccessToken();
    console.log("Authenticated successfully.");
    const nowStr = new Date().toISOString();

    console.log("Creating/updating sessions with integer fields...");
    
    // Session for Child 1 (איתי כהן), Instructor 2 (Christian)
    await createDocument(token, "TrainingSessions", "100", {
      SessionId: 100,
      TargetMetric: "הסתגלות וביטחון במים",
      EndTime: "17:00:00",
      Notes: "שיעור פרטי מתוכנן לשיפור סגנון ונשימות",
      ChildId: 1,
      ParentId: 3,
      Status: "Scheduled",
      MeetingDate: "2026-07-01",
      InstructorId: 2,
      StartTime: "16:00:00"
    });

    // Session for Child 3 (דניאל לוי), Instructor 2 (Christian)
    await createDocument(token, "TrainingSessions", "101", {
      SessionId: 101,
      TargetMetric: "ציפה על הבטן",
      EndTime: "18:00:00",
      Notes: "שיעור פרטי לעבודה על ציפה ויציבה עצמאית",
      ChildId: 3,
      ParentId: 4,
      Status: "Scheduled",
      MeetingDate: "2026-07-01",
      InstructorId: 2,
      StartTime: "17:00:00"
    });

    console.log("Database seeding completed successfully!");
  } catch (error) {
    console.error("Failed to seed database:", error);
  }
}

run();
