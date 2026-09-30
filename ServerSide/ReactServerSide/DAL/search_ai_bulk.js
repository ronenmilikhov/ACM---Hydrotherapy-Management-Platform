const fs = require('fs');
const path = require('path');

const controllerPath = path.join(__dirname, '..', 'Controllers', 'InstructorController.cs');
const content = fs.readFileSync(controllerPath, 'utf8');
const lines = content.split('\n');

let foundIndex = -1;
lines.forEach((line, idx) => {
  if (line.includes('ai-bulk-reports-draft')) {
    foundIndex = idx;
    console.log(`Found ai-bulk-reports-draft at line ${idx + 1}`);
  }
});

if (foundIndex !== -1) {
  for (let i = foundIndex - 5; i < Math.min(foundIndex + 60, lines.length); i++) {
    console.log(`${i + 1}: ${lines[i]}`);
  }
}
