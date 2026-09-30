const fs = require('fs');
const path = require('path');

const uiPath = path.join(__dirname, '..', '..', '..', 'Instructor', 'InstructorAIGroupReports.js');
const content = fs.readFileSync(uiPath, 'utf8');
const lines = content.split('\n');

const keywords = ["בחירת קבוצה", "בחירת תאריך", "נושא השיעור", "תיאור מהלך השיעור", "שם הילד", "נוכחות"];
lines.forEach((line, idx) => {
  keywords.forEach(kw => {
    if (line.includes(kw)) {
      console.log(`Line ${idx + 1}: ${line.trim()}`);
      for (let i = Math.max(0, idx - 4); i < Math.min(idx + 6, lines.length); i++) {
        console.log(`  ${i + 1}: ${lines[i]}`);
      }
    }
  });
});
