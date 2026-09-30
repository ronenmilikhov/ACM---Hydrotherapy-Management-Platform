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

const SkillAreaLabels = {
  "water_confidence": "הסתגלות וביטחון במים",
  "breathing": "שליטה בנשימות",
  "coordination": "תנועתיות וקואורדינציה",
  "floating": "יציבה וציפה",
  "communication": "תקשורת במים ושיתוף פעולה",
  "persistence": "התמדה ומאמץ",
  "initiative": "יוזמה",
  "focus": "קשב וריכוז",
  "instructions": "תגובה להוראות",
  "independence": "עצמאות בתרגיל"
};

const WeaknessGoals = {
  "water_confidence": "לפתח ביטחון בסיסי במים דרך תרגילים הדרגתיים ומשחקי מים",
  "breathing": "לשפר שליטה בנשימות ולהתרגל להכניס ראש למים בצורה מבוקרת",
  "coordination": "לחזק תנועתיות וקואורדינציה דרך תרגילי תנועה מגוונים",
  "floating": "לשפר יציבות וציפה דרך תרגול מודרג עם תמיכה הולכת ופוחתת",
  "communication": "לפתח תקשורת ושיתוף פעולה במים דרך עבודה בזוגות",
  "persistence": "לחזק התמדה ומאמץ דרך משימות קצרות עם תגמול מיידי",
  "initiative": "לעודד יוזמה עצמית דרך בחירת תרגילים ומשימות אישיות",
  "focus": "לשפר קשב וריכוז דרך הנחיות ברורות ומשימות ממוקדות",
  "instructions": "לחזק תגובה להוראות דרך תרגול שלבי עם חיזוקים חיוביים",
  "independence": "לפתח עצמאות בביצוע תרגילים עם הפחתה הדרגתית של סיוע"
};

const StrengthGoals = {
  "water_confidence": "להרחיב ביטחון במים לתרגילים מתקדמים ועומקים שונים",
  "breathing": "לשכלל טכניקת נשימה לשחייה רציפה ולמרחקים ארוכים",
  "coordination": "להעמיק קואורדינציה דרך תרגילים מורכבים ושילובי סגנונות",
  "floating": "להתקדם לציפה עצמאית ומעבר לתנוחות ציפה מתקדמות",
  "communication": "להוביל פעילויות קבוצתיות ולסייע לחברים בקבוצה",
  "persistence": "לקחת אתגרים מתקדמים ולהתמיד במשימות ארוכות יותר",
  "initiative": "להוביל תרגילים חדשים ולהציע רעיונות לפעילויות",
  "focus": "לשמור על ריכוז במשימות מורכבות ורב-שלביות",
  "instructions": "לסייע בהדגמת תרגילים ולשמש דוגמה לילדים אחרים",
  "independence": "לבצע רצפי תרגילים עצמאיים ולנהל אימון אישי"
};

function generatePersonalGoals(strengths, weaknesses) {
  const goals = [];
  if (weaknesses) {
    weaknesses.forEach(key => {
      if (WeaknessGoals[key]) {
        goals.push({
          area: key,
          areaLabel: SkillAreaLabels[key],
          type: "weakness",
          typeLabel: "חולשה - יעד לשיפור",
          goal: WeaknessGoals[key]
        });
      }
    });
  }
  if (strengths) {
    strengths.forEach(key => {
      if (StrengthGoals[key]) {
        goals.push({
          area: key,
          areaLabel: SkillAreaLabels[key],
          type: "strength",
          typeLabel: "חוזקה - יעד להתקדמות",
          goal: StrengthGoals[key]
        });
      }
    });
  }
  return JSON.stringify(goals);
}

async function run() {
  try {
    const token = await getAccessToken();
    console.log("Authenticated successfully.");
    const nowStr = new Date().toISOString();

    // 1. Seed second parent
    console.log("Seeding Yossi Levi (Parent 2)...");
    await createDocument(token, "Parents", "parent2@acm.com", {
      Email: "parent2@acm.com",
      PasswordHash: "pbkdf2$120000$gMCbcOXsjotZ2d2Waj6y0A==$b5qN8ieDASnZU27I7c/3+U+sb1DXwarCbxOxKwAFErM=", // Hashed 123456
      FirstName: "יוסי",
      LastName: "לוי",
      Phone: "054-9876543",
      IsActive: true,
      Id: 4,
      CreatedAt: nowStr
    });

    // 2. Seed 3 Groups
    console.log("Seeding groups...");
    await createDocument(token, "Groups", "1", {
      Id: 1,
      Name: "קבוצת כריש",
      Description: "קבוצה למתקדמים המתמקדת בשיפור סגנון וסיבולת",
      IsActive: true,
      CreatedAt: nowStr
    });

    await createDocument(token, "Groups", "2", {
      Id: 2,
      Name: "קבוצת דולפין",
      Description: "קבוצה לבינוניים המתמקדת בביטחון במים ובנשימות",
      IsActive: true,
      CreatedAt: nowStr
    });

    await createDocument(token, "Groups", "3", {
      Id: 3,
      Name: "קבוצת סוס ים",
      Description: "קבוצה למתחילים המתמקדת בהסתגלות למים וציפה",
      IsActive: true,
      CreatedAt: nowStr
    });

    // 3. Associate Instructor (Christian - Id: 2) with the Groups
    console.log("Associating Instructor (Christian) with groups...");
    await createDocument(token, "InstructorGroups", "1", {
      Id: 1,
      InstructorId: 2,
      GroupId: 1,
      IsActive: true,
      CreatedAt: nowStr
    });

    await createDocument(token, "InstructorGroups", "2", {
      Id: 2,
      InstructorId: 2,
      GroupId: 2,
      IsActive: true,
      CreatedAt: nowStr
    });

    await createDocument(token, "InstructorGroups", "3", {
      Id: 3,
      InstructorId: 2,
      GroupId: 3,
      IsActive: true,
      CreatedAt: nowStr
    });

    // 4. Seed Children
    console.log("Seeding children...");
    
    // Child 1 (Shlomi's child) -> Shark Group (Id: 1)
    await createDocument(token, "Children", "1", {
      Id: 1,
      ParentId: 3,
      FirstName: "איתי",
      LastName: "כהן",
      BirthDate: "2016-04-12",
      Description: "ילד חברותי שאוהב מים אך חושש מעט מצלילה",
      Strengths: JSON.stringify(["floating", "coordination"]),
      Weaknesses: JSON.stringify(["breathing"]),
      PersonalGoals: generatePersonalGoals(["floating", "coordination"], ["breathing"]),
      IsActive: true,
      CreatedAt: nowStr
    });

    // Child 2 (Shlomi's child) -> Dolphin Group (Id: 2)
    await createDocument(token, "Children", "2", {
      Id: 2,
      ParentId: 3,
      FirstName: "נועה",
      LastName: "כהן",
      BirthDate: "2018-08-25",
      Description: "ילדה אנרגטית, מסתגלת במהירות להנחיות",
      Strengths: JSON.stringify(["water_confidence", "instructions"]),
      Weaknesses: JSON.stringify(["coordination"]),
      PersonalGoals: generatePersonalGoals(["water_confidence", "instructions"], ["coordination"]),
      IsActive: true,
      CreatedAt: nowStr
    });

    // Child 3 (Yossi's child) -> Seahorse Group (Id: 3)
    await createDocument(token, "Children", "3", {
      Id: 3,
      ParentId: 4,
      FirstName: "דניאל",
      LastName: "לוי",
      BirthDate: "2017-11-03",
      Description: "אוהב לשחק במים, זקוק להרבה חיזוקים חיוביים",
      Strengths: JSON.stringify(["persistence"]),
      Weaknesses: JSON.stringify(["water_confidence"]),
      PersonalGoals: generatePersonalGoals(["persistence"], ["water_confidence"]),
      IsActive: true,
      CreatedAt: nowStr
    });

    // Child 4 (Yossi's child) -> Seahorse Group (Id: 3)
    await createDocument(token, "Children", "4", {
      Id: 4,
      ParentId: 4,
      FirstName: "מיה",
      LastName: "לוי",
      BirthDate: "2019-02-14",
      Description: "ילדה שקטה, אוהבת את שיעורי השחייה",
      Strengths: JSON.stringify(["instructions"]),
      Weaknesses: JSON.stringify(["coordination"]),
      PersonalGoals: generatePersonalGoals(["instructions"], ["coordination"]),
      IsActive: true,
      CreatedAt: nowStr
    });

    // 5. Associate Children with their Groups
    console.log("Associating children with groups...");
    await createDocument(token, "GroupChildren", "1_1", {
      GroupId: 1,
      ChildId: 1,
      IsActive: true,
      CreatedAt: nowStr
    });

    await createDocument(token, "GroupChildren", "2_2", {
      GroupId: 2,
      ChildId: 2,
      IsActive: true,
      CreatedAt: nowStr
    });

    await createDocument(token, "GroupChildren", "3_3", {
      GroupId: 3,
      ChildId: 3,
      IsActive: true,
      CreatedAt: nowStr
    });

    await createDocument(token, "GroupChildren", "3_4", {
      GroupId: 3,
      ChildId: 4,
      IsActive: true,
      CreatedAt: nowStr
    });

    console.log("Seeding completed successfully.");
  } catch (error) {
    console.error("Failed to seed data:", error);
  }
}

run();
