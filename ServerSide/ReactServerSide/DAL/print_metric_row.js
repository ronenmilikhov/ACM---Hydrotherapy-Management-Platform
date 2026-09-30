const fs = require('fs');
const path = require('path');

const uiPath = path.join(__dirname, '..', '..', '..', 'Instructor', 'InstructorAIGroupReports.js');
const content = fs.readFileSync(uiPath, 'utf8');
const lines = content.split('\n');

for (let i = 850; i < 885; i++) {
  console.log(`${i + 1}: ${lines[i]}`);
}
