const fs = require('fs');
const path = require('path');

const uiPath = path.join(__dirname, '..', '..', '..', 'Instructor', 'InstructorAIGroupReports.js');
const content = fs.readFileSync(uiPath, 'utf8');
const lines = content.split('\n');

lines.forEach((line, idx) => {
  if (line.includes('<TextInput')) {
    console.log(`Line ${idx + 1}:`);
    for (let i = idx; i < Math.min(idx + 12, lines.length); i++) {
      console.log(`  ${i + 1}: ${lines[i]}`);
    }
  }
});
