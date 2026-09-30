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

async function getCollection(token, collection) {
  const url = `https://firestore.googleapis.com/v1/projects/${projectId}/databases/(default)/documents/${collection}`;
  const res = await fetch(url, {
    headers: { Authorization: `Bearer ${token}` }
  });
  const data = await res.json();
  return data.documents || [];
}

async function debugLogic() {
  try {
    const token = await getAccessToken();
    console.log("Authenticated.");

    const children = await getCollection(token, "Children");
    const parents = await getCollection(token, "Parents");
    const trainingSessions = await getCollection(token, "TrainingSessions");
    const lessonInvitations = await getCollection(token, "LessonInvitations");
    const lessonInvitationRecipients = await getCollection(token, "LessonInvitationRecipients");

    console.log("Fetched collections. Replicating C# parsing...");

    // 1. Map children
    const childrenMap = {};
    for (const doc of children) {
      const fields = doc.fields || {};
      if (fields.Id && fields.Id.integerValue) {
        childrenMap[Number(fields.Id.integerValue)] = doc;
      }
    }

    // 2. Map parents
    const parentsMap = {};
    for (const doc of parents) {
      const fields = doc.fields || {};
      if (fields.Id && fields.Id.integerValue) {
        parentsMap[Number(fields.Id.integerValue)] = doc;
      }
    }

    const instructorId = "2";
    const startDate = new Date();
    startDate.setHours(0,0,0,0);
    const endDate = new Date(startDate);
    endDate.setDate(startDate.getDate() + 6);

    console.log(`Query range: ${startDate.toISOString().slice(0, 10)} to ${endDate.toISOString().slice(0, 10)}`);

    // 3. Filter TrainingSessions
    const sessions = [];
    const existingSessionsKeySet = new Set();

    for (const doc of trainingSessions) {
      const fields = doc.fields || {};
      const name = doc.name.split('/').pop();
      try {
        const instId = fields.InstructorId ? fields.InstructorId.integerValue : null;
        if (instId !== instructorId) continue;

        const meetingDateStr = fields.MeetingDate ? fields.MeetingDate.stringValue : null;
        if (!meetingDateStr) {
          throw new Error("Missing MeetingDate");
        }
        const meetingDate = new Date(meetingDateStr);
        if (meetingDate < startDate || meetingDate > endDate) continue;

        const status = fields.Status ? fields.Status.stringValue : "";
        if (status === "Cancelled") continue;

        // Parse fields exactly like GetInstructorTrainingSessions in DBServices.cs
        if (!fields.ParentId) {
          // In C# it checks: item.ContainsKey("ParentId") ? int.Parse(item["ParentId"].N) : 0
          console.log(`Document TrainingSessions/${name} is missing ParentId. Set to 0.`);
        } else if (!fields.ParentId.integerValue) {
          throw new Error(`ParentId has invalid type: ${JSON.stringify(fields.ParentId)}`);
        }

        if (!fields.ChildId || !fields.ChildId.integerValue) {
          throw new Error(`ChildId is missing or invalid: ${JSON.stringify(fields.ChildId)}`);
        }

        if (!fields.SessionId || !fields.SessionId.integerValue) {
          throw new Error(`SessionId is missing or invalid: ${JSON.stringify(fields.SessionId)}`);
        }

        const startTimeStr = fields.StartTime ? fields.StartTime.stringValue : null;
        const endTimeStr = fields.EndTime ? fields.EndTime.stringValue : null;
        if (!startTimeStr || !endTimeStr) {
          throw new Error(`Missing StartTime or EndTime`);
        }

        const childId = Number(fields.ChildId.integerValue);
        existingSessionsKeySet.add(`${childId}_${meetingDateStr}_${startTimeStr.slice(0,8)}_${endTimeStr.slice(0,8)}`);
        console.log(`Processed TrainingSession ${name}`);
      } catch (err) {
        console.error(`!!! CRASH in TrainingSessions/${name}:`, err.message);
      }
    }

    // 4. Map Recipients
    const recipientsMap = {};
    for (const doc of lessonInvitationRecipients) {
      const fields = doc.fields || {};
      const name = doc.name.split('/').pop();
      try {
        const isActive = fields.IsActive ? fields.IsActive.booleanValue : false;
        const responseStatus = fields.ResponseStatus ? fields.ResponseStatus.stringValue : "";
        if (!isActive || responseStatus !== "Approved") continue;

        if (!fields.InvitationId || !fields.InvitationId.integerValue) {
          throw new Error(`InvitationId is missing or invalid: ${JSON.stringify(fields.InvitationId)}`);
        }
        if (!fields.ChildId || !fields.ChildId.integerValue) {
          throw new Error(`ChildId is missing or invalid: ${JSON.stringify(fields.ChildId)}`);
        }
        if (!fields.ParentId || !fields.ParentId.integerValue) {
          throw new Error(`ParentId is missing or invalid: ${JSON.stringify(fields.ParentId)}`);
        }

        const invId = Number(fields.InvitationId.integerValue);
        if (!recipientsMap[invId]) {
          recipientsMap[invId] = [];
        }
        recipientsMap[invId].push(doc);
      } catch (err) {
        console.error(`!!! CRASH in LessonInvitationRecipients/${name}:`, err.message);
      }
    }

    // 5. Filter LessonInvitations
    for (const doc of lessonInvitations) {
      const fields = doc.fields || {};
      const name = doc.name.split('/').pop();
      try {
        const instId = fields.InstructorId ? fields.InstructorId.integerValue : null;
        if (instId !== instructorId) continue;

        const lessonType = fields.LessonType ? fields.LessonType.stringValue : "";
        if (lessonType !== "Private") continue;

        const meetingDateStr = fields.MeetingDate ? fields.MeetingDate.stringValue : null;
        if (!meetingDateStr) throw new Error("Missing MeetingDate");
        const meetingDate = new Date(meetingDateStr);
        if (meetingDate < startDate || meetingDate > endDate) continue;

        const status = fields.Status ? fields.Status.stringValue : "";
        if (status === "Cancelled") continue;

        if (!fields.InvitationId || !fields.InvitationId.integerValue) {
          throw new Error(`InvitationId is missing or invalid: ${JSON.stringify(fields.InvitationId)}`);
        }

        const startTimeStr = fields.StartTime ? fields.StartTime.stringValue : null;
        const endTimeStr = fields.EndTime ? fields.EndTime.stringValue : null;
        if (!startTimeStr || !endTimeStr) {
          throw new Error(`Missing StartTime or EndTime`);
        }

        const invitationId = Number(fields.InvitationId.integerValue);
        const recipients = recipientsMap[invitationId] || [];
        for (const lirItem of recipients) {
          const lirFields = lirItem.fields;
          const childId = Number(lirFields.ChildId.integerValue);
          const key = `${childId}_${meetingDateStr}_${startTimeStr.slice(0,8)}_${endTimeStr.slice(0,8)}`;
          if (existingSessionsKeySet.has(key)) continue;

          console.log(`Candidate LessonInvitation ${name} for Child ${childId}`);
        }
      } catch (err) {
        console.error(`!!! CRASH in LessonInvitations/${name}:`, err.message);
      }
    }

    console.log("Replication complete.");
  } catch (err) {
    console.error("Global Error:", err);
  }
}

debugLogic();
