const fs = require('fs');
const path = require('path');

const uiPath = path.join(__dirname, '..', '..', '..', 'Instructor', 'InstructorAIGroupReports.js');
const content = fs.readFileSync(uiPath, 'utf8');
const lines = content.split('\n');

function printRange(label, start, end) {
  console.log(`=== ${label} ===`);
  for (let i = start; i < end; i++) {
    console.log(`${i + 1}: ${lines[i]}`);
  }
}

lines.forEach((line, idx) => {
  if (line.includes('container:') || line.includes('setupCard:') || line.includes('draftCard:') || line.includes('draftCardHeader:')) {
    printRange(line.trim(), idx, idx + 15);
  }
});
