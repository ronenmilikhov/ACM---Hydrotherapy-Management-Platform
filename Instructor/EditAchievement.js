import React, { useRef, useEffect, useState } from 'react';
import {
  StyleSheet, Text, View, Animated, Dimensions, TouchableOpacity,
  StatusBar, Easing, Platform, TextInput, KeyboardAvoidingView, ScrollView
} from 'react-native';
import { LinearGradient } from 'expo-linear-gradient';
import { BlurView } from 'expo-blur';
import Svg, { Path } from 'react-native-svg';
import { useNavigation, useRoute } from '@react-navigation/native';
import AppCard from '../components/ui/AppCard';
import { colors, shadows } from '../theme/tokens';
import PrimaryButton from '../components/ui/PrimaryButton';

const { width, height } = Dimensions.get('window');

// ========================================
// 1. SEAMLESS ANIMATED WAVES
// ========================================
const Wave = ({ color, duration, startPosition, d }) => {
  const translateX = useRef(new Animated.Value(startPosition)).current;

  useEffect(() => {
    const toPosition = startPosition === 0 ? -600 : 0;
    Animated.loop(
      Animated.sequence([
        Animated.timing(translateX, {
          toValue: toPosition,
          duration: duration / 2,
          easing: Easing.inOut(Easing.sin),
          useNativeDriver: true,
        }),
        Animated.timing(translateX, {
          toValue: startPosition,
          duration: duration / 2,
          easing: Easing.inOut(Easing.sin),
          useNativeDriver: true,
        }),
      ]),
    ).start();
  }, [duration, startPosition]);

  return (
    <Animated.View style={[styles.waveContainer, { transform: [{ translateX }] }]}>
      <Svg width={2400} height={150} viewBox="0 0 2400 150" preserveAspectRatio="none">
        <Path fill={color} d={d} />
      </Svg>
    </Animated.View>
  );
};


// ========================================
// 3. MAIN COMPONENT
// ========================================
export default function EditAchievement() {
  const navigation = useNavigation();
  const route = useRoute();
  const achievement = route.params?.achievement || { label: '', info: '', icon: '🌟' };

  const [title, setTitle] = useState(achievement.label);
  const [info, setInfo] = useState(achievement.info);

  const onSave = route.params?.onSave;

  return (
    <View style={styles.container}>
      <StatusBar barStyle="light-content" backgroundColor="transparent" translucent={true} />

      {/* Deep Ocean Base */}
      <LinearGradient colors={['#005a80', '#00456a', colors.bgDeep]} style={StyleSheet.absoluteFill} />

      {/* Surface Water */}
      <LinearGradient colors={['#00aed8', '#007fa7', '#004d73']} style={[StyleSheet.absoluteFill, { opacity: 0.6 }]} />

      {/* Bottom Waves */}
      <Wave color="rgba(0, 153, 204, 0.3)" duration={8000} startPosition={0} d="M0,75 C200,45 400,105 600,75 C800,45 1000,105 1200,75 L1200,150 L0,150 Z M1200,75 C1400,45 1600,105 1800,75 C2000,45 2200,105 2400,75 L2400,150 L1200,150 Z" />
      <Wave color="rgba(0, 212, 255, 0.25)" duration={6000} startPosition={-600} d="M0,100 C300,60 500,130 800,90 C1000,60 1100,120 1200,100 L1200,150 L0,150 Z M1200,100 C1500,60 1700,130 2000,90 C2200,60 2300,120 2400,100 L2400,150 L1200,150 Z" />

      {/* Content wrapper */}
      <KeyboardAvoidingView 
        behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
        style={{ flex: 1, justifyContent: 'center' }}
      >
        <ScrollView 
          contentContainerStyle={styles.scrollContent}
          showsVerticalScrollIndicator={false}
          keyboardShouldPersistTaps="handled"
        >
          <AppCard useBlur blurIntensity={35} blurTint="dark" style={styles.card}>
            
            {/* Header */}
            <View style={styles.cardHeader}>
              <Text style={styles.title}>עריכת הישג הילד</Text>
              <TouchableOpacity onPress={() => navigation.goBack()} style={styles.closeButton}>
                <View style={styles.closeIconBg}>
                  <Text style={styles.closeText}>✕</Text>
                </View>
              </TouchableOpacity>
            </View>

            {/* Inputs */}
            <View style={styles.inputGroup}>
              <Text style={styles.label}>שם הישג</Text>
              <TextInput
                value={title}
                onChangeText={setTitle}
                style={styles.input}
                placeholder="הזן שם להישג"
                placeholderTextColor="rgba(255,255,255,0.4)"
              />
            </View>

            <View style={styles.inputGroup}>
              <Text style={styles.label}>פרטים נוספים / תיאור</Text>
              <TextInput
                value={info}
                onChangeText={setInfo}
                multiline
                numberOfLines={4}
                style={styles.textarea}
                placeholder="הזן פרטים מלאים"
                placeholderTextColor="rgba(255,255,255,0.4)"
              />
            </View>

            {/* Action Buttons */}
            <View style={styles.actionsRow}>
              <TouchableOpacity
                onPress={() => navigation.goBack()}
                style={styles.cancelButton}
              >
                <Text style={styles.cancelText}>בטל</Text>
              </TouchableOpacity>

              <PrimaryButton
                label="שמור שינויים"
                onPress={() => {
                  const updated = {
                    label: title,
                    info: info,
                    icon: achievement.icon || '🌟',
                  };
                  if (onSave) onSave(updated);
                  navigation.goBack();
                }}
                style={styles.saveButton}
                gradientStyle={styles.btnGradient}
                textStyle={{ color: '#001529', fontWeight: '800', fontSize: 16 }}
              />
            </View>
          </AppCard>
        </ScrollView>
      </KeyboardAvoidingView>
    </View>
  );
}

// ========================================
// STYLES
// ========================================
const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.bgDeep },
  waveContainer: { position: 'absolute', bottom: 0, left: (width - 2400) / 2, width: 2400, height: 150, zIndex: 0 },
  scrollContent: {
    flexGrow: 1,
    justifyContent: 'center',
    paddingHorizontal: 20,
    paddingVertical: 40,
    zIndex: 10,
  },
  
  card: {
    padding: 24,
    borderRadius: 24,
    backgroundColor: 'rgba(0, 40, 70, 0.75)',
    borderWidth: 1,
    borderColor: 'rgba(255, 255, 255, 0.15)',
    ...shadows.glowPrimary,
    elevation: 10,
  },
  cardHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 24,
    borderBottomWidth: 1,
    borderBottomColor: 'rgba(255, 255, 255, 0.1)',
    paddingBottom: 16,
  },
  title: {
    fontSize: 22,
    fontWeight: '800',
    color: '#00d4ff',
    writingDirection: 'rtl',
  },
  closeButton: { padding: 4 },
  closeIconBg: {
    width: 32, height: 32, borderRadius: 16,
    backgroundColor: 'rgba(255,255,255,0.1)',
    justifyContent: 'center', alignItems: 'center',
  },
  closeText: { color: 'rgba(255,255,255,0.8)', fontSize: 16, fontWeight: '700' },
  
  inputGroup: { marginBottom: 20 },
  label: {
    fontSize: 14,
    color: 'rgba(255, 255, 255, 0.7)',
    marginBottom: 8,
    writingDirection: 'rtl',
    textAlign: 'right',
    fontWeight: '600'
  },
  input: {
    borderWidth: 1,
    borderColor: 'rgba(255, 255, 255, 0.15)',
    borderRadius: 16,
    padding: 16,
    backgroundColor: 'rgba(255, 255, 255, 0.08)',
    color: '#fff',
    fontSize: 16,
    writingDirection: 'rtl',
    textAlign: 'right',
  },
  textarea: {
    borderWidth: 1,
    borderColor: 'rgba(255, 255, 255, 0.15)',
    borderRadius: 16,
    padding: 16,
    minHeight: 120,
    backgroundColor: 'rgba(255, 255, 255, 0.08)',
    color: '#fff',
    fontSize: 16,
    textAlignVertical: 'top',
    writingDirection: 'rtl',
    textAlign: 'right',
  },
  
  actionsRow: {
    flexDirection: 'row',
    marginTop: 10,
    gap: 12,
  },
  cancelButton: {
    flex: 1,
    backgroundColor: 'rgba(255, 255, 255, 0.08)',
    borderWidth: 1,
    borderColor: 'rgba(255, 255, 255, 0.15)',
    borderRadius: 18,
    height: 52,
    justifyContent: 'center',
    alignItems: 'center',
  },
  cancelText: {
    color: 'rgba(255, 255, 255, 0.8)',
    fontWeight: '700',
    fontSize: 16,
    writingDirection: 'rtl',
  },
  saveButton: {
    flex: 1,
    borderRadius: 18,
    overflow: 'hidden'
  },
  btnGradient: {
    height: 52,
    justifyContent: 'center',
    alignItems: 'center',
  }
});

