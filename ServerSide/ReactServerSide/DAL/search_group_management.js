const fs = require('fs');
const path = require('path');

const filePath = path.join(__dirname, '..', '..', '..', 'ServerSide', 'ReactServerSide', 'DAL', 'DBServices.cs');
const content = fs.readFileSync(filePath, 'utf8');
const lines = content.split('\n');

let startLine = -1;
lines.forEach((line, idx) => {
  if (line.includes('GetChildrenForGroupManagement')) {
    startLine = idx;
  }
});

if (startLine !== -1) {
  console.log(`Found GetChildrenForGroupManagement at line ${startLine + 1}`);
  for (let i = Math.max(0, startLine - 2); i < Math.min(lines.length, startLine + 50); i++) {
    console.log(`${i + 1}: ${lines[i]}`);
  }
} else {
  console.log("GetChildrenForGroupManagement not found");
}
