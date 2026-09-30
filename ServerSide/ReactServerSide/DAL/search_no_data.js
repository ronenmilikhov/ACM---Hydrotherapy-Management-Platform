const fs = require('fs');
const path = require('path');

function walk(dir) {
  let results = [];
  const list = fs.readdirSync(dir);
  list.forEach(file => {
    if (file === 'bin' || file === 'obj' || file === '.git' || file === 'node_modules' || file === 'ServerSide' || file === '.expo' || file === '.expo-shared') return;
    const fullPath = path.join(dir, file);
    const stat = fs.statSync(fullPath);
    if (stat && stat.isDirectory()) {
      results = results.concat(walk(fullPath));
    } else if (file.endsWith('.js')) {
      results.push(fullPath);
    }
  });
  return results;
}

const root = path.join(__dirname, '..', '..', '..');
const files = walk(root);

files.forEach(f => {
  const content = fs.readFileSync(f, 'utf8');
  if (content.includes('אין נתונים') || content.includes('חציון המדדים') || content.includes('אין נתונים זמינים')) {
    const lines = content.split('\n');
    lines.forEach((line, idx) => {
      if (line.includes('אין') || line.includes('נתונים') || line.includes('חציון') || line.includes('מדדים')) {
        console.log(`${path.relative(root, f)}:${idx + 1}: ${line.trim()}`);
      }
    });
  }
});
