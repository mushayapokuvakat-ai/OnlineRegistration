import os
from flask import Flask, request, jsonify, send_from_directory
from flask_sqlalchemy import SQLAlchemy
from flask_bcrypt import Bcrypt
from flask_jwt_extended import (JWTManager, create_access_token,
                                jwt_required, get_jwt_identity,
                                verify_jwt_in_request, get_jwt)
from flask_cors import CORS
from flask_limiter import Limiter
from flask_limiter.util import get_remote_address
from datetime import timedelta, datetime
from functools import wraps
from dotenv import load_dotenv
from flask_migrate import Migrate
from models import db, User, Course, Enrollment, Setting, College, Programme

load_dotenv()

app = Flask(__name__, static_folder='wwwroot', static_url_path='')
app.config['SECRET_KEY'] = os.getenv('SECRET_KEY', 'dev-secret')

# DATABASE_URL handling for Render/Heroku (standardizing 'postgresql://')
db_url = os.getenv('DATABASE_URL', 'sqlite:///online_reg.db')
if db_url.startswith("postgres://"):
    db_url = db_url.replace("postgres://", "postgresql://", 1)

app.config['SQLALCHEMY_DATABASE_URI'] = db_url
app.config['SQLALCHEMY_TRACK_MODIFICATIONS'] = False
app.config['JWT_SECRET_KEY'] = os.getenv('JWT_SECRET_KEY', 'jwt-dev-secret')
app.config['JWT_ACCESS_TOKEN_EXPIRES'] = timedelta(hours=1)

db.init_app(app)
migrate = Migrate(app, db)
bcrypt = Bcrypt(app)
jwt = JWTManager(app)
CORS(app)

limiter = Limiter(get_remote_address, app=app,
                  default_limits=["100 per minute"], storage_uri="memory://")


# --- Helper Decorators ---
def role_required(role):
    def wrapper(fn):
        @wraps(fn)
        def decorator(*args, **kwargs):
            verify_jwt_in_request()
            claims = get_jwt()
            if claims.get('role') != role:
                return jsonify({"success": False, "message": "Access denied"}), 403
            return fn(*args, **kwargs)
        return decorator
    return wrapper


# --- Security Middleware ---
@app.before_request
def security_shield():
    user_agent = request.headers.get('User-Agent', '').lower()
    illegal_bots = ["curl", "python", "postman", "go-http", "java", "wget", "httpclient"]
    if any(bot in user_agent for bot in illegal_bots):
        return "Automated access is blocked.", 403

    shield_key = os.getenv('SHIELD_KEY')
    request_key = request.headers.get('X-Shield-Key')
    is_api_request = request.path.startswith('/api')
    referring_site = request.headers.get('Referer', '')
    is_internal = request.host in referring_site

    if is_api_request and not is_internal and shield_key and request_key != shield_key:
        return "Security Alert: Connection blocked.", 403

    enable_whitelist = os.getenv('ENABLE_IP_WHITELIST', 'false').lower() == 'true'
    if enable_whitelist:
        allowed_ips = os.getenv('ALLOWED_IPS', '127.0.0.1,::1').split(',')
        client_ip = request.headers.get('X-Forwarded-For', request.remote_addr)
        if client_ip not in allowed_ips:
            return f"Access denied for IP {client_ip}.", 403


# --- Static File Serving ---
@app.route('/')
def serve_index():
    return send_from_directory('wwwroot', 'index.html')

@app.route('/<path:path>')
def serve_static(path):
    return send_from_directory('wwwroot', path)


# ━━━━ COLLEGE & PROGRAMME ROUTES ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
@app.route('/api/colleges', methods=['GET'])
def get_colleges():
    colleges = College.query.order_by(College.name).all()
    return jsonify({"success": True, "data": [c.to_dict() for c in colleges]})

@app.route('/api/colleges/<int:college_id>/programmes', methods=['GET'])
def get_programmes(college_id):
    programmes = Programme.query.filter_by(college_id=college_id).order_by(Programme.name).all()
    return jsonify({"success": True, "data": [p.to_dict() for p in programmes]})

@app.route('/api/programmes/<int:programme_id>/courses', methods=['GET'])
def get_programme_courses(programme_id):
    level = request.args.get('level', '1.1')
    courses = Course.query.filter_by(
        programme_id=programme_id, level=level, is_active=True
    ).all()
    return jsonify({
        "success": True,
        "data": [c.to_dict() for c in courses],
        "message": f"Found {len(courses)} course(s) for level {level}."
    })


# ━━━━ AUTH ROUTES ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
@app.route('/api/auth/register', methods=['POST'])
def register():
    data = request.get_json()
    if User.query.filter_by(email=data['email']).first():
        return jsonify({"success": False, "message": "Email already exists"}), 400

    hashed_password = bcrypt.generate_password_hash(data['password']).decode('utf-8')
    new_user = User(
        full_name=data['fullName'],
        email=data['email'],
        password_hash=hashed_password,
        course=data.get('course', 'N/A'),
        role=data.get('role', 'Student'),
        college_id=data.get('collegeId'),
        programme_id=data.get('programmeId'),
        level=data.get('level'),
    )
    db.session.add(new_user)
    db.session.commit()
    return jsonify({"success": True, "message": "Registration successful"}), 201

@app.route('/api/auth/login', methods=['POST'])
def login():
    data = request.get_json()
    user = User.query.filter_by(email=data['email']).first()
    if user and bcrypt.check_password_hash(user.password_hash, data['password']):
        access_token = create_access_token(
            identity=str(user.user_id),
            additional_claims={'role': user.role}
        )
        return jsonify({
            "success": True,
            "data": {
                "token": access_token,
                "fullName": user.full_name,
                "email": user.email,
                "role": user.role,
                "collegeID": user.college_id,
                "collegeName": user.college_rel.name if user.college_rel else None,
                "programmeID": user.programme_id,
                "programmeName": user.programme_rel.name if user.programme_rel else None,
                "level": user.level,
            }
        })
    return jsonify({"success": False, "message": "Invalid email or password"}), 401


# ━━━━ COURSE & ENROLLMENT ROUTES (Student) ━━━━━━━━━━━━━━━━━━━
@app.route('/api/courses', methods=['GET'])
def get_courses():
    programme_id = request.args.get('programmeId', type=int)
    level = request.args.get('level')
    query = Course.query.filter_by(is_active=True)
    if programme_id:
        query = query.filter_by(programme_id=programme_id)
    if level:
        query = query.filter_by(level=level)
    courses = query.all()
    return jsonify({
        "success": True,
        "data": [c.to_dict() for c in courses],
        "message": f"Found {len(courses)} course(s)."
    })

@app.route('/api/courses/my-courses', methods=['GET'])
@role_required('Student')
def get_my_courses():
    student_id = get_jwt_identity()
    enrollments = Enrollment.query.filter_by(student_id=student_id).all()
    return jsonify({
        "success": True,
        "data": [e.to_dict() for e in enrollments],
        "message": f"You are enrolled in {len(enrollments)} course(s)."
    })

@app.route('/api/courses/enroll', methods=['POST'])
@role_required('Student')
def enroll():
    student_id = get_jwt_identity()
    data = request.get_json()
    course_ids = data.get('courseIds', [])

    reg_open = Setting.query.get('RegistrationOpen')
    if reg_open and reg_open.value.lower() != 'true':
        return jsonify({"success": False, "message": "Registration is currently closed."}), 400

    max_courses_setting = Setting.query.get('MaxCoursesPerStudent')
    max_courses = int(max_courses_setting.value) if max_courses_setting else 7

    current_count = Enrollment.query.filter_by(student_id=student_id).count()
    if current_count + len(course_ids) > max_courses:
        return jsonify({"success": False,
                        "message": f"You cannot register for more than {max_courses} courses."}), 400

    enrolled_count = 0
    user = User.query.get(student_id)

    for cid in course_ids:
        course = Course.query.get(cid)
        if course and course.is_active and course.enrolled_count < course.max_capacity:
            existing = Enrollment.query.filter_by(student_id=student_id, course_id=cid).first()
            if not existing:
                enrollment = Enrollment(
                    student_id=student_id,
                    student_name=user.full_name,
                    student_email=user.email,
                    course_id=course.course_id,
                    course_name=course.course_name,
                    course_code=course.course_code
                )
                course.enrolled_count += 1
                db.session.add(enrollment)
                enrolled_count += 1

    db.session.commit()
    return jsonify({
        "success": True,
        "data": {"enrolledCourses": enrolled_count},
        "message": f"Successfully enrolled in {enrolled_count} course(s)."
    })

@app.route('/api/courses/enroll/<int:course_id>', methods=['DELETE'])
@role_required('Student')
def drop_course(course_id):
    student_id = get_jwt_identity()
    enrollment = Enrollment.query.filter_by(student_id=student_id, course_id=course_id).first()
    if enrollment:
        course = Course.query.get(course_id)
        if course:
            course.enrolled_count -= 1
        db.session.delete(enrollment)
        db.session.commit()
        return jsonify({"success": True, "message": "Course dropped successfully."})
    return jsonify({"success": False, "message": "You are not enrolled in this course."}), 404

@app.route('/api/courses/settings', methods=['GET'])
def get_settings_public():
    settings = {s.key: s.value for s in Setting.query.all()}
    return jsonify({"success": True, "data": settings})


# ━━━━ LECTURER ROUTES ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
@app.route('/api/lecturer/courses', methods=['GET'])
@role_required('Lecturer')
def get_lecturer_courses():
    lecturer_id = get_jwt_identity()
    courses = Course.query.filter_by(lecturer_id=lecturer_id).all()
    return jsonify({"success": True, "data": [c.to_dict() for c in courses]})

@app.route('/api/lecturer/courses/<int:id>/students', methods=['GET'])
@role_required('Lecturer')
def get_course_students(id):
    lecturer_id = int(get_jwt_identity())
    course = Course.query.get(id)
    if not course or course.lecturer_id != lecturer_id:
        return jsonify({"success": False, "message": "Access denied"}), 403
    enrollments = Enrollment.query.filter_by(course_id=id).all()
    students = [{
        "userID": e.student_id, "fullName": e.student_name,
        "email": e.student_email, "enrolledAt": e.enrolled_at.isoformat()
    } for e in enrollments]
    return jsonify({
        "success": True,
        "data": {
            "course": {"courseID": course.course_id, "courseName": course.course_name,
                       "courseCode": course.course_code},
            "students": students, "totalStudents": len(students)
        }
    })


# ━━━━ ADMIN ROUTES ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
@app.route('/api/admin/users', methods=['GET'])
@role_required('Admin')
def get_all_users():
    users = User.query.order_by(User.created_at.desc()).all()
    return jsonify({"success": True, "data": [u.to_dict() for u in users]})

@app.route('/api/admin/users/<int:id>', methods=['DELETE'])
@role_required('Admin')
def delete_user(id):
    user = User.query.get(id)
    if user:
        if user.role == 'Admin':
            return jsonify({"success": False, "message": "Cannot delete admin"}), 400
        db.session.delete(user)
        db.session.commit()
        return jsonify({"success": True, "message": "User deleted successfully."})
    return jsonify({"success": False, "message": "User not found"}), 404

@app.route('/api/admin/courses', methods=['GET'])
@role_required('Admin')
def get_all_courses_admin():
    courses = Course.query.all()
    return jsonify({"success": True, "data": [c.to_dict() for c in courses]})

@app.route('/api/admin/courses', methods=['POST'])
@role_required('Admin')
def add_course():
    data = request.get_json()
    new_course = Course(
        course_name=data['courseName'],
        course_code=data['courseCode'],
        description=data.get('description', ''),
        lecturer_id=data.get('lecturerID'),
        max_capacity=data.get('maxCapacity', 100),
        programme_id=data.get('programmeId'),
        level=data.get('level'),
    )
    db.session.add(new_course)
    db.session.commit()
    return jsonify({
        "success": True,
        "data": {"courseId": new_course.course_id},
        "message": "Course added successfully."
    })

@app.route('/api/admin/courses/<int:id>', methods=['DELETE'])
@role_required('Admin')
def delete_course(id):
    course = Course.query.get(id)
    if course:
        course.is_active = False
        db.session.commit()
        return jsonify({"success": True, "message": "Course removed successfully."})
    return jsonify({"success": False, "message": "Course not found"}), 404

@app.route('/api/admin/settings', methods=['GET'])
@role_required('Admin')
def get_settings():
    settings = {s.key: s.value for s in Setting.query.all()}
    return jsonify({"success": True, "data": settings})

@app.route('/api/admin/settings', methods=['PUT'])
@role_required('Admin')
def update_settings():
    data = request.get_json()
    for key, value in data.items():
        setting = Setting.query.get(key)
        if setting:
            setting.value = str(value).lower() if isinstance(value, bool) else str(value)
        else:
            db.session.add(Setting(key=key, value=str(value)))
    db.session.commit()
    return jsonify({"success": True, "message": "Settings updated successfully."})


# ━━━━ DATABASE SEEDING ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
def seed_db():
    from seed_data import COLLEGES, PROGRAMMES, COURSES as COURSE_DATA
    with app.app_context():
        db.create_all()

        # Admin user
        admin_email = "patnat@system.com"
        if not User.query.filter_by(email=admin_email).first():
            admin_hash = bcrypt.generate_password_hash("Pyruvate#13").decode('utf-8')
            admin = User(full_name='Patnat', email=admin_email,
                         password_hash=admin_hash, role='Admin')
            db.session.add(admin)

        # Seed colleges
        if not College.query.first():
            college_map = {}
            for cd in COLLEGES:
                c = College(name=cd['name'], code=cd['code'],
                            description=cd['description'], icon=cd['icon'])
                db.session.add(c)
                db.session.flush()
                college_map[cd['code']] = c.college_id

            # Seed programmes
            programme_map = {}
            for college_code, progs in PROGRAMMES.items():
                for pd in progs:
                    p = Programme(name=pd['name'], code=pd['code'],
                                  college_id=college_map[college_code])
                    db.session.add(p)
                    db.session.flush()
                    programme_map[pd['code']] = p.programme_id

            # Seed courses
            for prog_code, levels in COURSE_DATA.items():
                pid = programme_map.get(prog_code)
                if not pid:
                    continue
                for level, courses in levels.items():
                    for cname, ccode, cdesc in courses:
                        # Skip if code already exists (shared courses)
                        if not Course.query.filter_by(course_code=ccode).first():
                            course = Course(
                                course_name=cname, course_code=ccode,
                                description=cdesc, programme_id=pid,
                                level=level, max_capacity=100
                            )
                            db.session.add(course)

            # Seed default settings
            if not Setting.query.get('MaxCoursesPerStudent'):
                db.session.add(Setting(key='MaxCoursesPerStudent', value='7'))
            if not Setting.query.get('RegistrationOpen'):
                db.session.add(Setting(key='RegistrationOpen', value='true'))

            db.session.commit()
            print("Database seeded successfully!")


# --- Initialize Database on Startup ---
with app.app_context():
    seed_db()

@app.route('/health')
def health_check():
    return jsonify({"status": "healthy", "timestamp": datetime.now().isoformat()}), 200

if __name__ == '__main__':
    app.run(host='0.0.0.0', port=9091, debug=True)
