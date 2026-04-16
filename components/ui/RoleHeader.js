import React from 'react';
import { Text, TouchableOpacity, View, StyleSheet } from 'react-native';

export default function RoleHeader({
  title,
  subtitle,
  onMenuPress,
  onLeftPress,
  leftIcon = '‹',
  leftLabel,
  showLeftButton = true,
  onRightPress,
  rightIcon = '⌕',
  rightLabel,
  showRightButton = true,
  theme = 'light',
}) {
  const leftPressHandler = onLeftPress || onMenuPress;
  const isDark = theme === 'dark';

  return (
    <View style={[styles.header, isDark ? styles.headerDark : null]}>
      {showLeftButton ? (
        <TouchableOpacity
          activeOpacity={0.75}
          style={[styles.iconButton, isDark ? styles.iconButtonDark : null]}
          onPress={leftPressHandler}
          disabled={!leftPressHandler}
        >
          {!!leftIcon && <Text style={[styles.headerIcon, isDark ? styles.headerIconDark : null]}>{leftIcon}</Text>}
          {!!leftLabel && <Text style={[styles.leftLabel, isDark ? styles.leftLabelDark : null]}>{leftLabel}</Text>}
        </TouchableOpacity>
      ) : (
        <View style={styles.iconButton} />
      )}

      <View style={styles.headerCenter}>
        <Text style={[styles.headerTitle, isDark ? styles.headerTitleDark : null]}>{title}</Text>
        {!!subtitle && <Text style={[styles.headerSubtitle, isDark ? styles.headerSubtitleDark : null]}>{subtitle}</Text>}
      </View>

      {showRightButton ? (
        <TouchableOpacity
          activeOpacity={0.75}
          style={[styles.iconButton, isDark ? styles.iconButtonDark : null]}
          onPress={onRightPress}
          disabled={!onRightPress}
        >
          <Text style={[styles.headerIcon, isDark ? styles.headerIconDark : null]}>{rightIcon}</Text>
          {!!rightLabel && <Text style={[styles.searchLabel, isDark ? styles.searchLabelDark : null]}>{rightLabel}</Text>}
        </TouchableOpacity>
      ) : (
        <View style={styles.iconButton} />
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  header: {
    marginTop: 12,
    paddingHorizontal: 18,
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    zIndex: 12,
  },
  headerDark: {
    marginBottom: 2,
  },
  iconButton: {
    minWidth: 44,
    minHeight: 36,
    justifyContent: 'center',
    alignItems: 'center',
  },
  iconButtonDark: {
    borderRadius: 12,
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.15)',
    backgroundColor: 'rgba(255,255,255,0.08)',
    paddingHorizontal: 6,
  },
  headerIcon: {
    fontSize: 28,
    color: '#1D2935',
  },
  headerIconDark: {
    color: '#def6ff',
  },
  headerCenter: {
    alignItems: 'center',
    flex: 1,
  },
  headerTitle: {
    color: '#1F8FD7',
    fontSize: 32,
    fontWeight: '700',
    writingDirection: 'rtl',
    textAlign: 'center',
  },
  headerTitleDark: {
    color: '#f3fbff',
    textShadowColor: 'rgba(0, 212, 255, 0.35)',
    textShadowOffset: { width: 0, height: 0 },
    textShadowRadius: 10,
  },
  headerSubtitle: {
    marginTop: 2,
    color: '#7D95AA',
    fontSize: 12,
    writingDirection: 'rtl',
    textAlign: 'center',
  },
  headerSubtitleDark: {
    color: 'rgba(255,255,255,0.72)',
  },
  searchLabel: {
    color: '#517796',
    fontSize: 12,
    marginTop: 2,
    textAlign: 'center',
    writingDirection: 'rtl',
  },
  searchLabelDark: {
    color: 'rgba(255,255,255,0.68)',
  },
  leftLabel: {
    color: '#517796',
    fontSize: 11,
    marginTop: 1,
    textAlign: 'center',
    writingDirection: 'rtl',
  },
  leftLabelDark: {
    color: 'rgba(255,255,255,0.68)',
  },
});
