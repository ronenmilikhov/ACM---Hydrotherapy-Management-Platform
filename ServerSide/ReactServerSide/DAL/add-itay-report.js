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

    console.log("Seeding reports for child איתי כהן (ChildId: 1)...");
    
    // Report 1:
    await createDocument(token, "ReportChildren", "10", {
      ReportId: 10,
      ChildId: 1,
      GroupId: 1,
      InstructorId: 2,
      ReportDate: "2026-06-24",
      CreatedAt: "2026-06-24T17:30:00Z",
      IsPresent: true,
      ExerciseTitle: "נשימות ובועות",
      ExerciseKey: "breathing",
      Comment: "איתי התאמן היום על הכנסת ראש למים וביצוע בועות. הוא הראה ביטחון רב יותר והצליח לבצע 5 נשיפות רצופות בתוך המים.",
      Metrics: JSON.stringify([
        { label: "שליטה בנשימות", value: 4, isIncluded: true },
        { label: "הסתגלות וביטחון במים", value: 4, isIncluded: true },
        { label: "תנועתיות וקואורדינציה", value: 3, isIncluded: true }
      ])
    });

    // Report 2:
    await createDocument(token, "ReportChildren", "11", {
      ReportId: 11,
      ChildId: 1,
      GroupId: 1,
      InstructorId: 2,
      ReportDate: "2026-06-28",
      CreatedAt: "2026-06-28T18:00:00Z",
      IsPresent: true,
      ExerciseTitle: "ציפה על הגב",
      ExerciseKey: "floating",
      Comment: "תרגלנו שמירה על ציפה יציבה על הגב. איתי הראה שליטה מעולה במיקום הגוף ושמר על רוגע לאורך כל שלבי הציפה.",
      Metrics: JSON.stringify([
        { label: "יציבה וציפה", value: 5, isIncluded: true },
        { label: "קשב וריכוז", value: 4, isIncluded: true },
        { label: "עצמאות בתרגיל", value: 4, isIncluded: true }
      ])
    });

    console.log("Completed adding progress reports for Itay!");
  } catch (error) {
    console.error("Error running script:", error);
  }
}

run();
