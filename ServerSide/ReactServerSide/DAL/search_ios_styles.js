const fs = require('fs');
const path = require('path');

const uiPath = path.join(__dirname, '..', '..', '..', 'Instructor', 'InstructorAIGroupReports.js');
const content = fs.readFileSync(uiPath, 'utf8');
const lines = content.split('\n');

lines.forEach((line, idx) => {
  if (line.includes('Platform.OS') || line.includes('row-reverse') || line.includes('flexDirection') || line.includes('alignItems') || line.includes('I18nManager')) {
    console.log(`${idx + 1}: ${line.trim()}`);
  }
});
