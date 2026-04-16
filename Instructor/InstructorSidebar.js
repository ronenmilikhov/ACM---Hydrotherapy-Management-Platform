import React from 'react';
import { View, Text, TouchableOpacity, Modal } from 'react-native';

const menuItems = [
  { label: 'דף הבית של מדריך', screen: 'InstructorHomepage' },
  { label: 'בחר קבוצה', screen: 'SelectGroup' },
  { label: 'רשימת ילדים', screen: 'ChildList' },
  { label: 'עריכת הישג', screen: 'EditAchievement' },
];

export default function InstructorSidebar({ visible, onClose, navigation }) {
  return (
    <Modal visible={visible} transparent animationType="slide">
      <View style={{ flex: 1, backgroundColor: 'rgba(0,0,0,0.4)' }}>
        <View style={{ width: '72%', height: '100%', backgroundColor: '#fff', paddingTop: 46, paddingHorizontal: 14 }}>
          <View style={{ flexDirection: 'row', justifyContent: 'space-between', marginBottom: 18 }}>
            <Text style={{ fontSize: 18, fontWeight: '700', writingDirection: 'rtl' }}>ניווט מדריך</Text>
            <TouchableOpacity onPress={onClose} style={{ padding: 8 }}>
              <Text style={{ fontSize: 20 }}>✕</Text>
            </TouchableOpacity>
          </View>
          {menuItems.map((item) => (
            <TouchableOpacity
              key={item.screen}
              onPress={() => {
                onClose();
                navigation.navigate(item.screen, item.params || {});
              }}
              style={{ paddingVertical: 13, borderBottomColor: '#E2E8F2', borderBottomWidth: 1 }}
            >
              <Text style={{ color: '#1A79D3', fontSize: 16, fontWeight: '700', textAlign: 'right', writingDirection: 'rtl' }}>
                {item.label}
              </Text>
            </TouchableOpacity>
          ))}
        </View>
      </View>
    </Modal>
  );
}
