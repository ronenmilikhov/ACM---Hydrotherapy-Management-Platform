const fs = require('fs');
const path = require('path');

const filePath = path.join(__dirname, '..', '..', '..', 'ServerSide', 'ReactServerSide', 'DAL', 'DBServices.cs');
const content = fs.readFileSync(filePath, 'utf8');
const lines = content.split('\n');

let occurrences = [];
lines.forEach((line, idx) => {
  if (line.includes('public List<ChildGroupManagementRecord> GetChildrenForGroupManagement')) {
    occurrences.push(idx);
  }
});

occurrences.forEach(startLine => {
  console.log(`Found GetChildrenForGroupManagement definition at line ${startLine + 1}`);
  for (let i = startLine; i < Math.min(lines.length, startLine + 80); i++) {
    console.log(`${i + 1}: ${lines[i]}`);
  }
});
